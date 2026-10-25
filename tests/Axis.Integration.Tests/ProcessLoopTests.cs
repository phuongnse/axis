using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A process loops back to an earlier step through a task. Each pass writes its own history rows
/// and task, told apart by the instance revision, and a call step stages one call per pass. The
/// <c>Revise</c> process loops on one task. <c>Rework</c> calls a connector and then asks for a
/// check that may send it back. <c>maria</c> holds the role <c>finance</c>, which completes the
/// tasks and resolves and inspects <c>Rework</c>.
/// </summary>
public sealed class ProcessLoopTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Succeeded = """{ "reason": "Checked in the purchasing system", "outcome": "succeeded", "output": { "orderNumber": "PO-LOOP-1" } }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_task_that_loops_back_to_itself_writes_history_rows_and_a_task_for_each_pass()
    {
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "Revise", subject);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        await RunStepAsync();
        var first = await OpenTaskAsync(instance);
        await CompleteAsync(maria, first, "again");
        await RunStepAsync();
        var second = await OpenTaskAsync(instance);
        Assert.NotEqual(first, second);
        await CompleteAsync(maria, second, "accept");
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);

        Assert.Equal("completed", await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(
            2L,
            await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.process_tasks WHERE process_instance_id = @id AND step = 'review'", ("id", instance)));
        Assert.Equal(
            "completed,completed",
            await fixture.ScalarAsync(TenantA, "SELECT string_agg(state, ',') FROM axis.process_tasks WHERE process_instance_id = @id", ("id", instance)));

        // A task writes a row when it starts to wait and another when it is completed. Every row of the two passes has its own revision.
        var rows = (await fixture.HistoryAsync(TenantA, instance)).Where(row => row.Step == "review").ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(4, rows.Select(row => row.Revision).Distinct().Count());
        var waiting = rows.Where(row => row.Output!.Contains("\"waiting\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, waiting.Count);
        Assert.Equal(
            [first, second],
            waiting.Select(row => JsonDocument.Parse(row.Output!).RootElement.GetProperty("taskId").GetGuid()));
    }

    [Fact]
    public async Task A_call_in_a_loop_is_staged_again_each_pass_and_resolving_the_latest_one_leaves_the_first_unchanged()
    {
        var subject = await fixture.InsertRequestAsync(TenantA, null, amount: 100m);
        var instance = await fixture.StartAsync(TenantA, "Rework", subject);
        using var maria = fixture.CreateClient().SignedInAs("maria");

        // Pass one: the call is unknown, an operator settles it and the check sends the request back.
        await RunStepAsync();
        var first = await CallAsync(instance, 1);
        await ForceUnknownAsync(instance, first);
        using (var resolved = await ResolveAsync(maria, instance, first))
        {
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        }

        await RunStepAsync();
        await CompleteAsync(maria, await OpenTaskAsync(instance), "again");

        // Pass two stages a second call of the same step, because the first one succeeded.
        await RunStepAsync();
        var second = await CallAsync(instance, 2);
        Assert.NotEqual(first, second);
        var firstRow = await CallRowAsync(first);
        Assert.Equal("succeeded", firstRow.State);
        Assert.NotNull(firstRow.SettledAt);
        Assert.Equal(
            (2L, 2L),
            ((long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.connector_calls WHERE process_instance_id = @id AND step = 'send'", ("id", instance)))!,
             (long)(await fixture.ScalarAsync(TenantA, "SELECT count(DISTINCT staged_revision) FROM axis.connector_calls WHERE process_instance_id = @id AND step = 'send'", ("id", instance)))!));

        await ForceUnknownAsync(instance, second);
        using (var resolved = await ResolveAsync(maria, instance, second))
        {
            Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        }

        Assert.Equal(firstRow, await CallRowAsync(first));
        var secondRow = await CallRowAsync(second);
        Assert.Equal("succeeded", secondRow.State);
        Assert.NotNull(secondRow.SettledAt);

        // The inspector lists both passes' calls, oldest first.
        using var request = RecordApiFixture.Request($"/api/apps/StepApp/processes/Rework/instances/{instance:D}", Host(TenantA));
        using var response = await maria.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            [first, second],
            body.RootElement.GetProperty("calls").EnumerateArray().Select(call => call.GetProperty("id").GetGuid()));
        await fixture.RunUntilIdleAsync(TenantA, CancellationToken);
    }

    // Runs the next process step, so no staged call is sent to the unbound connector URL.
    private async Task RunStepAsync() =>
        Assert.True(await fixture.RunNextStepAsync(TenantA, CancellationToken), "No process step is due.");

    private async Task<Guid> OpenTaskAsync(Guid instance) =>
        (Guid)(await fixture.ScalarAsync(TenantA, "SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;

    // The call of the step 'send' staged by the pass number, after checking that the step has staged that many.
    private async Task<Guid> CallAsync(Guid instance, int passes)
    {
        Assert.Equal(
            (long)passes,
            await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.connector_calls WHERE process_instance_id = @id AND step = 'send'", ("id", instance)));
        return (Guid)(await fixture.ScalarAsync(
            TenantA, "SELECT id FROM axis.connector_calls WHERE process_instance_id = @id AND step = 'send' ORDER BY staged_revision DESC LIMIT 1", ("id", instance)))!;
    }

    // Sets the call unknown with no work item, as a connector with no lookup leaves it.
    private async Task ForceUnknownAsync(Guid instance, Guid call)
    {
        await fixture.ScalarAsync(TenantA, "UPDATE axis.connector_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync(TenantA, "DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
    }

    private async Task<CallRow> CallRowAsync(Guid call) => new(
        (string)(await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call)))!,
        await fixture.ScalarAsync(TenantA, "SELECT settled_at FROM axis.connector_calls WHERE id = @id", ("id", call)));

    private static async Task<HttpResponseMessage> ResolveAsync(HttpClient client, Guid instance, Guid call)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post, $"/api/apps/StepApp/processes/Rework/instances/{instance:D}/calls/{call:D}/resolve", Host(TenantA), Succeeded);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task CompleteAsync(HttpClient client, Guid task, string outcome)
    {
        using var request = RecordApiFixture.Request(
            HttpMethod.Post, $"/api/apps/StepApp/tasks/{task:D}/complete", Host(TenantA), $$"""{ "outcome": "{{outcome}}" }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record CallRow(string State, object? SettledAt);
}
