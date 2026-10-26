using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// A process without a subject starts with parameter values and runs on the engine. Its instance has a
/// null subject and holds the parameters, its start condition reads the parameters, and only one instance of
/// it runs or waits at a time. The test activates a copy of StepApp with the <c>Tally</c> process, so the new
/// process stays in this class's database.
/// </summary>
public sealed class ProcessWithoutSubjectTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string Tally = """
        {
          "id": "4b8d2e6a-7c1f-4a93-8e05-1f6a3c9d7b52",
          "kind": "process",
          "name": "Tally",
          "formatVersion": 1,
          "parameters": [{ "name": "days", "type": "integer" }],
          "startCondition": {
            "expression": "days > 0",
            "message": { "textKey": "request.amountNegative" }
          },
          "start": "check",
          "steps": [
            { "name": "check", "type": "decision", "branches": [{ "when": "days > 5", "next": "late" }], "otherwise": "early" },
            { "name": "late", "type": "end" },
            { "name": "early", "type": "end" }
          ]
        }
        """;

    private const string Dated = """
        {
          "id": "7e3a9c1d-5b2f-4d68-a014-9c8b7a6d5e43",
          "kind": "process",
          "name": "Dated",
          "formatVersion": 1,
          "parameters": [{ "name": "days", "type": "integer" }],
          "startCondition": {
            "expression": "days > 0 and today() > date('2000-01-01')",
            "message": { "textKey": "request.amountNegative" }
          },
          "start": "check",
          "steps": [
            { "name": "check", "type": "decision", "branches": [{ "when": "toDate(now()) > date('2000-01-01')", "next": "late" }], "otherwise": "early" },
            { "name": "late", "type": "end" },
            { "name": "early", "type": "end" }
          ]
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_start_with_parameters_runs_a_decision_over_a_parameter_to_the_end()
    {
        var (releaseId, model) = await ActivateAsync();

        var (result, instanceId) = await StartAsync(model, releaseId, 7);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(ProcessStartOutcome.Started, result.Outcome);
        var instance = await fixture.InstanceAsync(instanceId);
        Assert.Equal(("completed", "late", true), (instance.State, instance.Step, instance.Ended));
        Assert.Equal(1, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE id = @id AND subject_id IS NULL AND subject_entity_id IS NULL AND parameters->>'days' = '7'",
            ("id", instanceId)));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["check", "late"], history.Select(row => row.Step));
        Assert.All(history, row => Assert.Null(row.Error));
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        var found = await ProcessStarts.FindInstanceAsync(connection, instanceId, CancellationToken);
        Assert.NotNull(found);
        Assert.Null(found.SubjectEntityId);
        Assert.Null(found.SubjectId);
        Assert.Equal("completed", found.State);
    }

    [Fact]
    public async Task A_second_start_while_the_first_instance_is_open_is_already_running()
    {
        var (releaseId, model) = await ActivateAsync();
        var before = await InstanceCountAsync(model);

        var (first, _) = await StartAsync(model, releaseId, 3);
        var (second, _) = await StartAsync(model, releaseId, 9);

        Assert.Equal(ProcessStartOutcome.Started, first.Outcome);
        Assert.Equal(ProcessStartOutcome.AlreadyRunning, second.Outcome);
        Assert.Equal(before + 1, await InstanceCountAsync(model));

        // The first instance ends, so a later start can run.
        await fixture.RunUntilIdleAsync(CancellationToken);
        var (third, _) = await StartAsync(model, releaseId, 3);
        Assert.Equal(ProcessStartOutcome.Started, third.Outcome);
        await fixture.RunUntilIdleAsync(CancellationToken);
    }

    [Fact]
    public async Task A_start_condition_that_is_false_over_the_parameters_writes_nothing()
    {
        var (releaseId, model) = await ActivateAsync();
        var instances = await InstanceCountAsync(model);
        var items = await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items");
        var audits = await StartedAuditCountAsync(model);

        var (result, instanceId) = await StartAsync(model, releaseId, 0);

        Assert.Equal(ProcessStartOutcome.ConditionFailed, result.Outcome);
        Assert.Equal("request.amountNegative", result.TextKey);
        Assert.Null(result.ErrorKind);
        Assert.Equal(0, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE id = @id", ("id", instanceId)));
        Assert.Equal(instances, await InstanceCountAsync(model));
        Assert.Equal(items, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items"));
        Assert.Equal(audits, await StartedAuditCountAsync(model));
    }

    [Fact]
    public async Task A_start_condition_and_a_decision_over_the_parameters_can_use_today_and_to_date()
    {
        var (releaseId, model) = await fixture.ActivateCopyWithFilesAsync(("processes/dated.json", Dated));

        var (result, instanceId) = await StartAsync(model, releaseId, 1, "Dated");
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(ProcessStartOutcome.Started, result.Outcome);
        Assert.Null(result.ErrorKind);
        var instance = await fixture.InstanceAsync(instanceId);
        Assert.Equal(("completed", "late", true), (instance.State, instance.Step, instance.Ended));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["check", "late"], history.Select(row => row.Step));
        Assert.All(history, row => Assert.Null(row.Error));
    }

    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync() =>
        await fixture.ActivateCopyWithFilesAsync(("processes/tally.json", Tally));

    /// <summary>Starts <paramref name="processName"/> with <paramref name="days"/> in its own transaction and commits it.</summary>
    private async Task<(ProcessStartResult Result, Guid InstanceId)> StartAsync(ApplicationModel model, Guid releaseId, long days, string processName = "Tally")
    {
        Assert.True(model.TryGetProcess(processName, out var process));
        var instanceId = Guid.CreateVersion7();
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await ProcessStarter.StartWithParametersAsync(
            connection,
            transaction,
            fixture.Tenant,
            model,
            releaseId,
            process,
            new JsonObject { ["days"] = days },
            instanceId,
            "anna",
            null,
            null,
            CancellationToken);
        await transaction.CommitAsync(CancellationToken);
        return (result, instanceId);
    }

    private Task<long> InstanceCountAsync(ApplicationModel model)
    {
        Assert.True(model.TryGetProcess("Tally", out var process));
        return fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", process.Id));
    }

    private Task<long> StartedAuditCountAsync(ApplicationModel model)
    {
        Assert.True(model.TryGetProcess("Tally", out var process));
        return fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE action = @action AND details->>'processId' = @process",
            ("action", AuditActions.ProcessStarted),
            ("process", process.Id.ToString("D")));
    }
}
