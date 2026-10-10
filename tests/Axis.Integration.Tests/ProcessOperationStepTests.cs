using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class ProcessOperationStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Operation_step_sets_the_subject_fields_and_writes_the_record_and_step_audit_records()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Submit", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var record = await fixture.RequestAsync(subjectId);
        Assert.Equal(2L, record.Version);
        Assert.Equal("submitted", record.Values["status"]!.GetValue<string>());
        Assert.Equal($"Request {subjectId:N}", record.Values["title"]!.GetValue<string>());

        // now() is the step transaction's start time, which the history stores as started_at.
        var startedAt = Assert.IsType<DateTime>(await fixture.ScalarAsync(
            "SELECT started_at FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'submit'",
            ("id", instanceId)));
        Assert.Equal(
            new DateTimeOffset(startedAt),
            DateTimeOffset.Parse(record.Values["submittedAt"]!.GetValue<string>(), CultureInfo.InvariantCulture));

        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["submit", "done"], history.Select(row => row.Step));
        Assert.Equal(1L, JsonNode.Parse(history[0].Input)!["subjectVersion"]!.GetValue<long>());
        Assert.Equal("""{"next": "done", "subjectVersion": 2}""", await OutputAsync(instanceId, "submit"));
        Assert.Null(history[0].Error);

        // Both audit records of the step share its transaction time, so their order is not asserted.
        Assert.Equal(
            [AuditActions.ProcessCompleted, AuditActions.ProcessStepCompleted, AuditActions.RecordUpdated],
            (await fixture.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        Assert.Equal("""{"next": "done", "step": "submit"}""", await SystemDetailsAsync(instanceId, AuditActions.ProcessStepCompleted));
        Assert.Equal("""{"fields": ["status", "submittedAt"], "version": 2}""", await SystemDetailsAsync(instanceId, AuditActions.RecordUpdated));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE action = @action AND actor = 'system' AND entity_id = @entity AND record_id = @record",
                ("action", AuditActions.RecordUpdated),
                ("entity", RequestEntityId),
                ("record", subjectId)));
    }

    [Fact]
    public async Task Operation_whose_value_breaks_a_validation_fails_the_instance_and_leaves_the_record_unchanged()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Break", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Failed, 2L, "lower", true), await fixture.InstanceAsync(instanceId));
        var record = await fixture.RequestAsync(subjectId);
        Assert.Equal(1L, record.Version);
        Assert.Equal(500m, record.Values["amount"]!.GetValue<decimal>());
        Assert.Null(record.Values["status"]);

        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("lower", row.Step);
        Assert.Equal("The record update was rejected: /values/amount: request.amountNegative", row.Error);
        Assert.Null(row.Output);
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE action = @action AND record_id = @record",
                ("action", AuditActions.RecordUpdated),
                ("record", subjectId)));
    }

    [Fact]
    public async Task Operation_after_a_user_update_writes_on_top_of_the_latest_version()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Submit", subjectId, fixture.FirstReleaseId);
        await using (var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(CancellationToken))
        {
            Assert.Equal(2L, await fixture.UpdateTitleAsync(transaction, subjectId, 1, "Changed by a user"));
            await transaction.CommitAsync(CancellationToken);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var record = await fixture.RequestAsync(subjectId);
        Assert.Equal(3L, record.Version);
        Assert.Equal("Changed by a user", record.Values["title"]!.GetValue<string>());
        Assert.Equal("submitted", record.Values["status"]!.GetValue<string>());
        Assert.Equal([("anna", 2L), ("system", 3L)], await RecordUpdatedVersionsAsync(subjectId));
    }

    [Fact]
    public async Task Operation_that_meets_an_uncommitted_user_update_waits_for_it_and_writes_the_next_version()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Submit", subjectId, fixture.FirstReleaseId);
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        Assert.Equal(2L, await fixture.UpdateTitleAsync(transaction, subjectId, 1, "Changed by a user"));

        // The step's lock on the subject row waits for the user's transaction, then reads version 2.
        var run = fixture.RunNextAsync(CancellationToken);
        await fixture.WaitUntilBlockedOnLockAsync(run, CancellationToken);
        await transaction.CommitAsync(CancellationToken);
        Assert.True(await run);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var record = await fixture.RequestAsync(subjectId);
        Assert.Equal((3L, "Changed by a user", "submitted"), (record.Version, record.Values["title"]!.GetValue<string>(), record.Values["status"]!.GetValue<string>()));
        Assert.Equal(2L, JsonNode.Parse((await fixture.HistoryAsync(instanceId))[0].Input)!["subjectVersion"]!.GetValue<long>());
        Assert.Equal([("anna", 2L), ("system", 3L)], await RecordUpdatedVersionsAsync(subjectId));
    }

    private Guid RequestEntityId
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Request", out var request));
            return request.Id;
        }
    }

    private async Task<string?> OutputAsync(Guid instanceId, string step) =>
        (string?)await fixture.ScalarAsync(
            "SELECT output::text FROM axis.process_step_history WHERE process_instance_id = @id AND step = @step",
            ("id", instanceId),
            ("step", step));

    private async Task<string?> SystemDetailsAsync(Guid instanceId, string action) =>
        (string?)await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instanceId),
            ("action", action));

    /// <summary>The actor and the version of each <c>record.updated</c> audit record of the request, by version.</summary>
    private async Task<IReadOnlyList<(string Actor, long Version)>> RecordUpdatedVersionsAsync(Guid subjectId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT actor, (details->>'version')::bigint FROM axis.audit_records
            WHERE action = @action AND record_id = @record ORDER BY 2
            """);
        command.Parameters.AddWithValue("action", AuditActions.RecordUpdated);
        command.Parameters.AddWithValue("record", subjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var versions = new List<(string, long)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            versions.Add((reader.GetString(0), reader.GetInt64(1)));
        }

        return versions;
    }
}
