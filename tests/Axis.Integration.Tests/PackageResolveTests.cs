using System.Net;
using System.Text.Json;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An operator with a <c>resolve</c> grant settles a package operation call whose outcome is unknown, through the
/// HTTP API. The sample's <c>assignPaymentReference</c> step runs an operation of the payment references package.
/// <c>requester</c> starts the approvals, <c>manager-2</c> is the manager and <c>finance-reviewer</c> has the grant. Each test forces the
/// call to <c>unknown</c> and deletes its work item, as an operation that is not idempotent leaves it. Each test leaves
/// no due work item behind, so the next one finds only its own.
/// </summary>
public sealed class PackageResolveTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Records = "/api/apps/PurchaseRequests/entities/PurchaseRequest/records";

    private const string Succeeded = """{ "reason": "Checked with the payment team", "outcome": "succeeded", "output": { "creditorReference": "RF18PR0001" } }""";

    private const string Failed = """{ "reason": "The reference was never issued", "outcome": "failed" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolving_an_unknown_package_call_as_succeeded_writes_the_output_and_the_instance_runs_on()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);
        Assert.Null(await PaymentReferenceAsync(requester, subject));

        using (var response = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(subject, body.RootElement.GetProperty("subjectId").GetGuid());
            Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("succeeded", await CallStateAsync(call));
        Assert.Null(await fixture.ScalarAsync("SELECT input FROM axis.package_calls WHERE id = @id", ("id", call)));
        Assert.NotNull(await fixture.ScalarAsync("SELECT settled_at FROM axis.package_calls WHERE id = @id", ("id", call)));
        Assert.Equal("RF18PR0001", await PaymentReferenceAsync(requester, subject));

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Equal("RF18PR0001", await PaymentReferenceAsync(requester, subject));
        Assert.Equal(0L, await DueWorkItemsAsync(instance));
        Assert.Equal(
            1L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'operation.callResolved'", ("id", instance)));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'operation.called'", ("id", instance)));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'assignPaymentReference' AND output->>'resolved' = 'true' AND output->>'outcome' = 'succeeded'",
                ("id", instance)));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id", ("id", call)));
    }

    [Fact]
    public async Task Resolving_as_failed_fails_the_instance_clears_the_input_and_audits_the_user()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);

        using (var response = await ResolveAsync(financeReviewer, instance, call, Failed))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("failed", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal("failed", await CallStateAsync(call));
        Assert.Null(await fixture.ScalarAsync("SELECT input FROM axis.package_calls WHERE id = @id", ("id", call)));
        Assert.Null(await PaymentReferenceAsync(requester, subject));
        Assert.Equal(0L, await DueWorkItemsAsync(instance));
        Assert.Equal(
            1L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'operation.callResolved'", ("id", instance)));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'operation.called'", ("id", instance)));
        Assert.Equal(("finance-reviewer", null, subject), await AuditActorAsync(instance, "operation.callResolved"));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'operation.callResolved'", ("id", instance)))!);

        // The details are jsonb, which does not keep the order of the properties.
        Assert.Equal(
            ["operation", "operationId", "outcome", "package", "reason", "step"],
            details.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("assignPaymentReference", details.RootElement.GetProperty("step").GetString());
        Assert.Equal("payment-references", details.RootElement.GetProperty("package").GetString());
        Assert.Equal("creditorReference", details.RootElement.GetProperty("operation").GetString());
        Assert.Equal(call, details.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("failed", details.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("The reference was never issued", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'assignPaymentReference' AND output->>'resolved' = 'true' AND output->>'outcome' = 'failed'",
                ("id", instance)));
        Assert.Equal(
            ("finance-reviewer", null, subject),
            await AuditActorAsync(instance, "process.failed"));
    }

    [Fact]
    public async Task A_resolve_whose_output_does_not_fit_the_manifest_is_400_and_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);
        var revision = await fixture.ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", ("id", instance));
        var history = await HistoryAsync(instance);
        var audit = await AuditAsync(instance);
        var input = await fixture.ScalarAsync("SELECT input::text FROM axis.package_calls WHERE id = @id", ("id", call));
        Assert.NotNull(input);

        foreach (var (body, key, message) in new[]
        {
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { "creditorReference": 5 } }""", "/output/creditorReference", "Must be a text value."),
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { } }""", "/output/creditorReference", "Required."),
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { "creditorReference": "RF18PR0001", "extra": 1 } }""", "/output/extra", "Unknown property."),
        })
        {
            using var response = await ResolveAsync(financeReviewer, instance, call, body);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        }

        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal(input, await fixture.ScalarAsync("SELECT input::text FROM axis.package_calls WHERE id = @id", ("id", call)));
        Assert.Equal("waiting", await StateAsync(instance));
        Assert.Equal(revision, await fixture.ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(audit, await AuditAsync(instance));
        Assert.Null(await PaymentReferenceAsync(requester, subject));
    }

    [Fact]
    public async Task A_user_without_the_grant_and_a_package_call_that_is_not_unknown_change_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (_, instance, call) = await UnknownCallAsync(requester);
        var revision = await fixture.ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", ("id", instance));
        var history = await HistoryAsync(instance);
        var audit = await AuditAsync(instance);

        foreach (var client in new[] { requester, manager2 })
        {
            using var denied = await ResolveAsync(client, instance, call, Succeeded);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        // A call of another instance is no call of this one.
        var (_, _, otherCall) = await UnknownCallAsync(requester);
        using (var elsewhere = await ResolveAsync(financeReviewer, instance, otherCall, Succeeded))
        {
            Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        }

        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal("unknown", await CallStateAsync(otherCall));

        // A pending call is refused with 409.
        await fixture.ScalarAsync("UPDATE axis.package_calls SET state = 'pending' WHERE id = @id", ("id", call));
        using (var pending = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(pending, HttpStatusCode.Conflict);
            Assert.Equal("The call's outcome is not unknown.", problem.RootElement.GetProperty("title").GetString());
        }

        await fixture.ScalarAsync("UPDATE axis.package_calls SET state = 'unknown' WHERE id = @id", ("id", call));
        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal(revision, await fixture.ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(audit, await AuditAsync(instance));
    }

    // Starts an approval, approves it as the manager and runs it until the package call is staged. The call is
    // then set to unknown with no work item, as an operation that is not idempotent leaves it.
    private async Task<(Guid Subject, Guid Instance, Guid Call)> UnknownCallAsync(HttpClient requester)
    {
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;
        using (var approved = await CompleteAsync(manager2, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await fixture.RunKindsUntilIdleAsync(CancellationToken, ProcessStarts.StepWorkItemKind, ProcessStarts.ConnectorCallWorkItemKind);
        Assert.Equal("waiting", await StateAsync(instance));
        Assert.Equal("assignPaymentReference", await fixture.ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", ("id", instance)));
        var call = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instance)))!;
        await fixture.ScalarAsync("UPDATE axis.package_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
        return (subject, instance, call);
    }

    private static async Task<string?> PaymentReferenceAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{Records}/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        return values.TryGetProperty("paymentReference", out var reference) ? reference.GetString() : null;
    }

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));

    private async Task<string?> CallStateAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.package_calls WHERE id = @id", ("id", call));

    private Task<long> HistoryAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance));

    private Task<long> AuditAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", instance));

    private Task<long> DueWorkItemsAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));

    private async Task<(string? Actor, string? Name, Guid? Record)> AuditActorAsync(Guid instance, string action)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT actor, actor_name, record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = @action",
            connection);
        command.Parameters.AddWithValue("id", instance);
        command.Parameters.AddWithValue("action", action);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetGuid(2));
    }
}
