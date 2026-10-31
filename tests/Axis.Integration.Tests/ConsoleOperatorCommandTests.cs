using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The operator console's routes that retry, cancel and resolve an instance. <c>platform-operator</c> holds only the role that
/// <c>Console:OperatorRole</c> names, so it holds no grant in the purchase request sample, and <c>requester</c> is not the
/// operator. The instances are the ones the process inspector tests use: a failed step, a waiting approval and a call
/// whose outcome is unknown. Each test leaves no due work item behind, so the next one finds only its own.
/// </summary>
public sealed class ConsoleOperatorCommandTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Path = "/api/console/instances/";

    private const string Reason = """{ "reason": "Checked in the console" }""";

    private const string Succeeded = """{ "reason": "Checked in the purchasing system", "outcome": "succeeded", "output": { "orderNumber": "PO-CONSOLE-1" } }""";

    private static readonly string _departments = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("ebe6bc20-4772-4d6c-839b-65d426ebeeca")));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_operator_retries_a_failed_instance_with_no_grant_and_the_audit_names_the_operator()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);

        // The operator holds no grant in the application: the process inspector's route refuses the same user.
        using (var inspector = await SendAsync("platform-operator", $"/api/apps/PurchaseRequests/processes/PurchaseRequestApproval/instances/{instance:D}/retry", Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, inspector.StatusCode);
        }

        Assert.Equal("failed", await StateAsync(instance));

        using (var response = await SendAsync("platform-operator", $"{Path}{instance:D}/retry", Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("PurchaseRequestApproval", body.RootElement.GetProperty("process").GetString());
            Assert.Equal(subject, body.RootElement.GetProperty("subjectId").GetGuid());
            Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("running", await StateAsync(instance));
        Assert.Equal(1L, await AuditCountAsync(instance, "process.retried"));
        Assert.Equal(("platform-operator", null, subject), await AuditActorAsync(instance, "process.retried"));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.retried'", ("id", instance)))!);
        Assert.Equal("managerApproval", details.RootElement.GetProperty("step").GetString());
        Assert.Equal("Checked in the console", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'retry' AND step = 'managerApproval'",
                ("id", instance)));

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
    }

    [Fact]
    public async Task The_operator_cancels_a_waiting_instance_and_the_audit_names_the_operator()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        var task = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;

        using (var response = await SendAsync("platform-operator", $"{Path}{instance:D}/cancel", Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("cancelled", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("cancelled", await StateAsync(instance));
        Assert.Equal("cancelled", await fixture.ScalarAsync("SELECT state FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Equal(1L, await AuditCountAsync(instance, "process.cancelled"));
        Assert.Equal(("platform-operator", null, subject), await AuditActorAsync(instance, "process.cancelled"));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)))!);
        Assert.Equal("Checked in the console", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'cancel' AND step = 'managerApproval'",
                ("id", instance)));
        await fixture.RunUntilIdleAsync(CancellationToken);
    }

    [Fact]
    public async Task The_operator_resolves_an_unknown_call_and_the_audit_and_history_name_the_operator()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (subject, instance, call) = await UnknownCallAsync(requester);

        using (var response = await SendAsync("platform-operator", $"{Path}{instance:D}/calls/{call:D}/resolve", Succeeded))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("succeeded", await fixture.ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call)));
        Assert.Equal("PO-CONSOLE-1", (await NumbersAsync(requester, subject)).PurchaseOrderNumber);
        Assert.Equal(1L, await AuditCountAsync(instance, "connector.callResolved"));
        Assert.Equal(("platform-operator", null, subject), await AuditActorAsync(instance, "connector.callResolved"));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'connector.callResolved'", ("id", instance)))!);
        Assert.Equal("Checked in the purchasing system", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal("succeeded", details.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'createPurchaseOrder' AND output->>'resolved' = 'true' AND output->>'outcome' = 'succeeded'",
                ("id", instance)));

        await RunToEndAsync(instance);
        Assert.Equal("completed", await StateAsync(instance));
    }

    [Fact]
    public async Task A_command_without_a_reason_or_the_csrf_header_or_from_a_user_without_the_role_is_refused_and_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);
        var (_, waiting, call) = await UnknownCallAsync(requester);
        var routes = new[]
        {
            ($"{Path}{instance:D}/retry", Reason, instance),
            ($"{Path}{instance:D}/cancel", Reason, instance),
            ($"{Path}{waiting:D}/cancel", Reason, waiting),
            ($"{Path}{waiting:D}/calls/{call:D}/resolve", Succeeded, waiting),
        };
        var before = new List<(string State, long History, long Audit)>();
        foreach (var (_, _, target) in routes)
        {
            before.Add(await SnapshotAsync(target));
        }

        using var signedOut = fixture.CreateClient();
        using var operatorClient = fixture.CreateClient().SignedInAs("platform-operator");
        using var user = fixture.CreateClient().SignedInAs("requester");
        foreach (var (path, body, _) in routes)
        {
            using var missingReason = await operatorClient.SendAsync(RecordApiFixture.Request(HttpMethod.Post, path, Host, "{}"), CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

            using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            withoutCsrf.Headers.Host = Host;
            using var noHeader = await operatorClient.SendAsync(withoutCsrf, CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);

            using var forbidden = await user.SendAsync(RecordApiFixture.Request(HttpMethod.Post, path, Host, body), CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using var unauthorized = await signedOut.SendAsync(RecordApiFixture.Request(HttpMethod.Post, path, Host, body), CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        }

        for (var index = 0; index < routes.Length; index++)
        {
            Assert.Equal(before[index], await SnapshotAsync(routes[index].Item3));
        }

        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal("waiting", await StateAsync(waiting));
        Assert.Equal("unknown", await fixture.ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call)));
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
    }

    [Fact]
    public async Task A_command_for_an_unknown_or_malformed_instance_or_call_is_a_404()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (_, instance, _) = await UnknownCallAsync(requester);

        foreach (var path in new[]
        {
            $"{Path}{Guid.NewGuid():D}/retry",
            $"{Path}{Guid.NewGuid():D}/cancel",
            $"{Path}nope/cancel",
            $"{Path}{Guid.NewGuid():D}/calls/{Guid.NewGuid():D}/resolve",
            $"{Path}{instance:D}/calls/nope/resolve",
        })
        {
            using var response = await SendAsync("platform-operator", path, path.EndsWith("/resolve", StringComparison.Ordinal) ? Succeeded : Reason);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal("waiting", await StateAsync(instance));
    }

    // Starts an approval for a request whose department has no manager, and runs it until managerApproval fails.
    private async Task<(Guid Subject, Guid Instance)> FailAsync(HttpClient requester)
    {
        var department = Guid.NewGuid();
        await fixture.ScalarAsync(
            $"INSERT INTO {_departments} ({EntityNaming.Quote(EntityNaming.IdColumn)}, {EntityNaming.Quote(EntityNaming.Column("code"))}, {EntityNaming.Quote(EntityNaming.Column("name"))}) VALUES (@id, @code, 'No manager')",
            ("id", department),
            ("code", "T" + department.ToString("N")[..10]));
        var subject = await CreateRequestAsync(requester, 500, department);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal("managerApproval", await fixture.ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", ("id", instance)));
        return (subject, instance);
    }

    // Starts an approval, approves it as the manager and runs it until the purchase order call is staged. The call is
    // then set to unknown with no work item, as a connector with no lookup leaves it.
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

        await fixture.RunStepsUntilIdleAsync(CancellationToken);

        // The approval's webhook delivery is no work item of the instance, so it is delivered here and not by a later run.
        await fixture.RunKindsUntilIdleAsync(CancellationToken, ProcessStarts.WebhookDeliverWorkItemKind);
        Assert.Equal("waiting", await StateAsync(instance));
        var call = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instance)))!;
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
        return (subject, instance, call);
    }

    // Runs the due work items until the instance is completed or failed.
    private async Task RunToEndAsync(Guid instance)
    {
        for (var round = 0; round < 20; round++)
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
            if (await StateAsync(instance) is "completed" or "failed")
            {
                return;
            }

            await fixture.ScalarAsync(
                "UPDATE axis.process_work_items SET due_at = now() WHERE process_instance_id = @id AND connector_call_id IS NOT NULL", ("id", instance));
        }

        Assert.Fail("The instance did not complete or fail within 20 rounds.");
    }

    private async Task<HttpResponseMessage> SendAsync(string user, string path, string body)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        return await client.SendAsync(RecordApiFixture.Request(HttpMethod.Post, path, Host, body), CancellationToken);
    }

    private async Task<(string State, long History, long Audit)> SnapshotAsync(Guid instance) =>
        ((await StateAsync(instance))!,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance)),
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", instance)));

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));

    private Task<long> AuditCountAsync(Guid instance, string action) =>
        fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", ("id", instance), ("action", action));

    private async Task SetDepartmentAsync(Guid subject, Guid department)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5")))}
            SET {EntityNaming.Quote(EntityNaming.Column("department"))} = @value, {EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1
            WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id
            """,
            connection);
        command.Parameters.AddWithValue("value", department);
        command.Parameters.AddWithValue("id", subject);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private async Task<(string? Actor, string? Name, Guid? Record)> AuditActorAsync(Guid instance, string action)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT actor, actor_name, record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = @action", connection);
        command.Parameters.AddWithValue("id", instance);
        command.Parameters.AddWithValue("action", action);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetGuid(2));
    }
}
