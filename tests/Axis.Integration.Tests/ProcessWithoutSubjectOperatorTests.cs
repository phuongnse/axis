using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Cancel, retry and resolve act on an instance of a process without a subject with only the user's grant,
/// because there is no record to read. <c>NightlyOrder</c> is such a process, added to StepApp in an edited
/// release: a decision, then a call to the <c>Purchasing</c> connector. <c>maria</c> holds the role
/// <c>finance</c>, which has every operator grant on it, and <c>rita</c> holds none. The tests run the next step
/// only, so no call is sent to the unbound connector URL.
/// </summary>
public sealed class ProcessWithoutSubjectOperatorTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Instances = "/api/apps/StepApp/processes/NightlyOrder/instances";

    private const string Reason = """{ "reason": "Checked by the operator" }""";

    private const string Succeeded = """{ "reason": "Checked in the purchasing system", "outcome": "succeeded", "output": { "orderNumber": "PO-1" } }""";

    private static readonly string _financePath = Path.Combine("policies", "finance-access.json");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static readonly string _nightlyOrder =
        """
        {
          "id": "c1f3a0b2-7d54-4e8a-9b36-2f5d8e1a4c70",
          "kind": "process",
          "name": "NightlyOrder",
          "formatVersion": 1,
          "parameters": [
            { "name": "reference", "type": "text" },
            { "name": "amount", "type": "decimal" },
            { "name": "divisor", "type": "integer" }
          ],
          "start": "check",
          "steps": [
            { "name": "check", "type": "decision", "branches": [{ "when": "amount / divisor > 0", "next": "send" }], "otherwise": "send" },
            {
              "name": "send",
              "type": "operation",
              "operation": "callConnector",
              "connector": { "name": "Purchasing", "operation": "createOrder" },
              "input": { "reference": "reference", "amount": "amount" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    [Fact]
    public async Task A_user_with_the_cancel_grant_cancels_a_running_instance_without_a_subject_and_a_user_without_it_gets_403()
    {
        await ActivateAsync();
        var instance = await StartAsync(divisor: 1);

        using (var refused = await PostAsync("rita", instance, "cancel", Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        Assert.Equal("running", await StateAsync(instance));

        using var cancelled = await PostAsync("maria", instance, "cancel", Reason);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var body = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subjectId").ValueKind);
        Assert.Equal("cancelled", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("cancelled", await StateAsync(instance));
        Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, "process.cancelled"));
        Assert.Null(await fixture.ScalarAsync(
            TenantA, "SELECT record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)));
    }

    [Fact]
    public async Task A_user_with_the_retry_grant_retries_a_failed_instance_without_a_subject()
    {
        await ActivateAsync();
        var instance = await FailAsync();

        using var response = await PostAsync("maria", instance, "retry", Reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subjectId").ValueKind);
        Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("running", await StateAsync(instance));
        Assert.Equal(
            1L,
            await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'process.step'", ("id", instance)));
        Assert.Single(await fixture.AuditRecordsAsync(TenantA, instance, "process.retried"));
    }

    [Fact]
    public async Task A_retry_without_a_subject_is_refused_while_another_instance_of_the_process_is_running()
    {
        await ActivateAsync();
        var failed = await FailAsync();
        await StartAsync(divisor: 1);

        using var response = await PostAsync("maria", failed, "retry", Reason);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("A running or waiting instance of this process already exists.", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("failed", await StateAsync(failed));
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, failed, "process.retried"));
    }

    [Fact]
    public async Task A_user_with_the_resolve_grant_resolves_an_unknown_call_of_an_instance_without_a_subject()
    {
        await ActivateAsync();
        var instance = await StartAsync(divisor: 1);
        await RunStepAsync();
        await RunStepAsync();
        Assert.Equal("waiting", await StateAsync(instance));
        var call = (Guid)(await fixture.ScalarAsync(
            TenantA, "SELECT id FROM axis.connector_calls WHERE process_instance_id = @id AND step = 'send'", ("id", instance)))!;
        await fixture.ScalarAsync(TenantA, "UPDATE axis.connector_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync(TenantA, "DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));

        using (var refused = await PostAsync("rita", instance, $"calls/{call:D}/resolve", Succeeded))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var response = await PostAsync("maria", instance, $"calls/{call:D}/resolve", Succeeded);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("subjectId").ValueKind);
        Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("succeeded", await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call)));
        Assert.Equal("done", await fixture.ScalarAsync(TenantA, "SELECT step FROM axis.process_instances WHERE id = @id", ("id", instance)));
    }

    // Gives finance every operator grant on NightlyOrder in an edited release. The same content reuses the stored release.
    private async Task ActivateAsync()
    {
        await fixture.ActivateEditedAsync(
            TenantA,
            (path, content) => path == _financePath
                ? content.Replace(
                    "{ \"name\": \"approveStart\", \"process\": \"Approve\", \"actions\": [\"start\"] },",
                    "{ \"name\": \"approveStart\", \"process\": \"Approve\", \"actions\": [\"start\"] },\n    { \"process\": \"NightlyOrder\", \"actions\": [\"start\", \"cancel\", \"retry\", \"resolve\"] },",
                    StringComparison.Ordinal)
                : content,
            added: new Dictionary<string, string> { ["processes/nightly-order.json"] = _nightlyOrder });

        // Every test starts with no running or waiting instance, so a start is never refused.
        await fixture.ScalarAsync(
            TenantA,
            "DELETE FROM axis.process_work_items WHERE process_instance_id IN (SELECT id FROM axis.process_instances WHERE state IN ('running', 'waiting'))");
        await fixture.ScalarAsync(TenantA, "UPDATE axis.process_instances SET state = 'completed' WHERE state IN ('running', 'waiting')");
    }

    private async Task<Guid> StartAsync(int divisor)
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var request = RecordApiFixture.Request(
            HttpMethod.Post,
            Instances,
            Host(TenantA),
            $$"""{ "parameters": { "reference": "N1", "amount": 10, "divisor": {{divisor}} } }""");
        using var response = await maria.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // A zero divisor makes the decision fail, so the instance ends up failed.
    private async Task<Guid> FailAsync()
    {
        var instance = await StartAsync(divisor: 0);
        await RunStepAsync();
        Assert.Equal("failed", await StateAsync(instance));
        return instance;
    }

    private async Task RunStepAsync() =>
        Assert.True(await fixture.RunNextStepAsync(TenantA, CancellationToken), "No process step is due.");

    private async Task<object?> StateAsync(Guid instance) =>
        await fixture.ScalarAsync(TenantA, "SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));

    private async Task<HttpResponseMessage> PostAsync(string user, Guid instance, string command, string body)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request(HttpMethod.Post, $"{Instances}/{instance:D}/{command}", Host(TenantA), body);
        return await client.SendAsync(request, CancellationToken);
    }
}
