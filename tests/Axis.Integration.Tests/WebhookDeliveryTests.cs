using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// The delivery of a raised event to its webhooks. StepApp's <c>Announce</c> raises <c>RequestNoted</c> with
/// <c>ratio</c> as the amount over the divisor, and the fixture's release gives that event two webhooks: <c>HookOne</c>
/// at <c>/one</c> and <c>HookTwo</c> at <c>/two</c>, whose secret binding holds two secrets.
/// </summary>
public sealed class WebhookDeliveryTests(WebhookDeliveryFixture fixture) : IClassFixture<WebhookDeliveryFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Raising_an_event_sends_each_webhook_one_signed_post_with_the_documented_body_and_headers()
    {
        await fixture.ClearDeliveriesAsync();
        var (subjectId, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);

        Assert.Equal(2L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.webhook_deliveries WHERE event_id = @event", ("event", eventId)));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunDeliveryAsync(CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunDeliveryAsync(CancellationToken));
        Assert.Null(await fixture.RunDeliveryAsync(CancellationToken));

        var one = Assert.Single(RequestsOf(WebhookDeliveryFixture.OnePath, eventId));
        var two = Assert.Single(RequestsOf(WebhookDeliveryFixture.TwoPath, eventId));
        foreach (var (request, webhook, secrets) in new[]
        {
            (one, "HookOne", new[] { fixture.OneSecret }),
            (two, "HookTwo", new[] { fixture.TwoSecrets.Prefixed, fixture.TwoSecrets.Plain }),
        })
        {
            Assert.Equal("POST", request.Method);
            Assert.Equal("application/json", request.Headers["Content-Type"]);
            var webhookId = Assert.Single(fixture.Model.Webhooks, candidate => candidate.Name == webhook).Id;
            Assert.Equal($"{eventId:D}_{webhookId:D}", request.Headers["webhook-id"]);
            Assert.True(long.TryParse(request.Headers["webhook-timestamp"], CultureInfo.InvariantCulture, out var timestamp));
            Assert.InRange(timestamp, DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());

            var body = Assert.IsType<JsonObject>(JsonNode.Parse(request.Body));
            Assert.Equal(["type", "id", "timestamp", "data"], body.Select(property => property.Key));
            Assert.Equal("RequestNoted", body["type"]!.GetValue<string>());
            Assert.Equal(eventId.ToString("D"), body["id"]!.GetValue<string>());
            Assert.EndsWith("Z", body["timestamp"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.True(DateTimeOffset.TryParse(body["timestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var raisedAt));
            Assert.InRange(raisedAt, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
            var data = Assert.IsType<JsonObject>(body["data"]);
            Assert.Equal("Request", data["entity"]!.GetValue<string>());
            Assert.Equal(subjectId.ToString("D"), data["subjectId"]!.GetValue<string>());
            var fields = Assert.IsType<JsonObject>(data["fields"]);
            Assert.Equal($"Request {subjectId:N}", fields["title"]!.GetValue<string>());
            Assert.Equal(125m, fields["ratio"]!.GetValue<decimal>());

            Assert.True(
                VerifiesWithStandardWebhooks(request, secrets),
                $"No signature of {webhook}'s request is valid for any of its secrets.");
        }

        // The staged rows are settled, and no item is left.
        Assert.Equal(2L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.webhook_deliveries WHERE event_id = @event AND state = 'delivered' AND attempts = 1 AND status_code = 200", ("event", eventId)));
        Assert.Equal(0L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'"));
    }

    [Fact]
    public async Task A_raise_that_fails_at_run_time_stages_no_delivery()
    {
        await fixture.ClearDeliveriesAsync();
        var (_, instanceId) = await RaiseAsync(500m, 0, run: false);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunStepAsync(CancellationToken));

        Assert.Equal(ProcessStarts.Failed, (await fixture.Tenant.InstanceAsync(instanceId)).State);
        Assert.Equal(0L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.webhook_deliveries"));
        Assert.Equal(0L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'"));
        Assert.Equal(0L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.events WHERE process_instance_id = @instance", ("instance", instanceId)));
    }

    [Fact]
    public async Task A_receiver_that_answers_500_gets_the_same_webhook_id_and_body_again_with_a_later_timestamp()
    {
        await fixture.ClearDeliveriesAsync();
        fixture.Receiver.Respond(WebhookDeliveryFixture.OnePath, 500);
        var (_, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);

        // The first item is the first webhook's, the delivery that answers 500.
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunDeliveryAsync(CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunDeliveryAsync(CancellationToken));
        var failedDelivery = await fixture.Tenant.ScalarAsync(
            "SELECT id FROM axis.webhook_deliveries WHERE event_id = @event AND state = 'pending'",
            ("event", eventId));
        Assert.Equal(
            (1, 500, 1),
            await DeliveryAsync(eventId, "pending"));
        var failures = await fixture.Tenant.ScalarAsync(
            "SELECT attempts FROM axis.delivery_failures WHERE kind = 'webhook.deliver' AND target = @target",
            ("target", ((Guid)failedDelivery!).ToString("D")));
        Assert.Equal(1, failures);

        await fixture.MakeDueAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunDeliveryAsync(CancellationToken));

        var requests = RequestsOf(WebhookDeliveryFixture.OnePath, eventId);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0].Headers["webhook-id"], requests[1].Headers["webhook-id"]);
        Assert.Equal(requests[0].Body, requests[1].Body);
        Assert.True(long.Parse(requests[1].Headers["webhook-timestamp"], CultureInfo.InvariantCulture) > long.Parse(requests[0].Headers["webhook-timestamp"], CultureInfo.InvariantCulture));
        Assert.NotEqual(requests[0].Headers["webhook-signature"], requests[1].Headers["webhook-signature"]);
        Assert.All(requests, request => Assert.True(VerifiesWithStandardWebhooks(request, [fixture.OneSecret])));
        Assert.Equal(0L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver'"));
        Assert.Equal((2, 200, 1), await DeliveryAsync(eventId, "delivered", WebhookDeliveryFixture.OnePath));
    }

    [Fact]
    public async Task A_secret_binding_with_two_secrets_gives_two_signatures_that_are_each_valid_for_one_secret()
    {
        await fixture.ClearDeliveriesAsync();
        var (_, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);
        await fixture.RunDeliveryAsync(CancellationToken);
        await fixture.RunDeliveryAsync(CancellationToken);

        var request = Assert.Single(RequestsOf(WebhookDeliveryFixture.TwoPath, eventId));

        var signatures = request.Headers["webhook-signature"].Split(' ');
        Assert.Equal(2, signatures.Length);
        Assert.All(signatures, signature => Assert.StartsWith("v1,", signature, StringComparison.Ordinal));
        Assert.Equal(signatures[0], Signature(request, fixture.TwoSecrets.Prefixed));
        Assert.Equal(signatures[1], Signature(request, fixture.TwoSecrets.Plain));
        Assert.NotEqual(signatures[0], signatures[1]);

        // One secret gives one signature.
        var single = Assert.Single(RequestsOf(WebhookDeliveryFixture.OnePath, eventId));
        Assert.Equal(Signature(single, fixture.OneSecret), Assert.Single(single.Headers["webhook-signature"].Split(' ')));
    }

    [Fact]
    public async Task A_delivered_request_writes_one_audit_record_as_system_that_holds_no_body()
    {
        await fixture.ClearDeliveriesAsync();
        var (subjectId, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);
        await fixture.RunDeliveryAsync(CancellationToken);
        await fixture.RunDeliveryAsync(CancellationToken);

        Assert.Equal(
            2L,
            await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.audit_records WHERE action = @action AND process_instance_id = @instance", ("action", AuditActions.WebhookDelivered), ("instance", instanceId)));
        var hookOne = Assert.Single(fixture.Model.Webhooks, webhook => webhook.Name == "HookOne");
        var record = await fixture.Tenant.ScalarAsync(
            "SELECT actor || '|' || record_id::text || '|' || details::text FROM axis.audit_records WHERE action = @action AND process_instance_id = @instance AND details ->> 'webhook' = 'HookOne'",
            ("action", AuditActions.WebhookDelivered),
            ("instance", instanceId));
        var parts = ((string)record!).Split('|', 3);
        Assert.Equal(("system", subjectId.ToString()), (parts[0], parts[1]));
        var details = Assert.IsType<JsonObject>(JsonNode.Parse(parts[2]));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["webhookId"] = hookOne.Id, ["webhook"] = "HookOne", ["eventId"] = eventId, ["statusCode"] = 200 },
            details));

        // Nothing of the body or of the payload is kept in the delivery's rows, its audit records or its logs.
        var kept = (string)(await fixture.Tenant.ScalarAsync(
            """
            SELECT (SELECT coalesce(string_agg(d::text, ' '), '') FROM axis.webhook_deliveries d)
                || (SELECT coalesce(string_agg(a.details::text, ' '), '') FROM axis.audit_records a WHERE a.action = 'webhook.delivered')
                || (SELECT coalesce(string_agg(f::text, ' '), '') FROM axis.delivery_failures f)
            """))!;
        Assert.DoesNotContain($"Request {subjectId:N}", kept, StringComparison.Ordinal);
        Assert.DoesNotContain("body", kept, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains($"Request {subjectId:N}", StringComparison.Ordinal) || entry.Contains(fixture.OneSecret, StringComparison.Ordinal) || entry.Contains(fixture.Receiver.BaseUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_fails_the_attempt()
    {
        await fixture.ClearDeliveriesAsync();
        fixture.Receiver.Respond(WebhookDeliveryFixture.OnePath, 302);
        var (_, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunDeliveryAsync(CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunDeliveryAsync(CancellationToken));

        // The redirect points back at the same path, so a client that followed it would have sent a second request.
        Assert.Single(RequestsOf(WebhookDeliveryFixture.OnePath, eventId));
        Assert.Equal((1, 302, 1), await DeliveryAsync(eventId, "pending"));
        Assert.Equal(
            "HTTP 302 (redirects are not followed)",
            await fixture.Tenant.ScalarAsync("SELECT last_error FROM axis.delivery_failures WHERE kind = 'webhook.deliver' AND attempts = 1"));
        Assert.Equal(
            1L,
            await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.audit_records WHERE action = @action AND process_instance_id = @instance", ("action", AuditActions.WebhookDelivered), ("instance", instanceId)));
        Assert.Equal(1L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver' AND claim_token IS NULL AND due_at > now()"));
    }

    [Fact]
    public async Task A_worker_outside_development_refuses_an_http_url_and_sends_nothing()
    {
        await fixture.ClearDeliveriesAsync();
        var (_, instanceId) = await RaiseAsync(500m, 4);
        var eventId = await EventIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunDeliveryOutsideDevelopmentAsync(CancellationToken));
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunDeliveryOutsideDevelopmentAsync(CancellationToken));

        Assert.DoesNotContain(fixture.Receiver.Requests, request => request.Headers.TryGetValue("webhook-id", out var id) && id.StartsWith(eventId.ToString("D"), StringComparison.Ordinal));
        Assert.Equal(2L, await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.webhook_deliveries WHERE event_id = @event AND state = 'pending' AND attempts = 1", ("event", eventId)));
        Assert.Equal(
            ["The url binding 'HookOneUrl' is not an https URL. Only Development allows http.", "The url binding 'HookTwoUrl' is not an https URL. Only Development allows http."],
            (await fixture.Tenant.ScalarAsync("SELECT string_agg(last_error, '|' ORDER BY last_error) FROM axis.delivery_failures WHERE kind = 'webhook.deliver'") as string)!.Split('|'));
    }

    /// <summary>Inserts a request, starts <c>Announce</c> for it and, unless <paramref name="run"/> is off, runs its step.</summary>
    private async Task<(Guid SubjectId, Guid InstanceId)> RaiseAsync(decimal amount, long divisor, bool run = true)
    {
        var started = await fixture.StartAnnounceAsync(amount, divisor);
        if (run)
        {
            Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));
        }

        return started;
    }

    private async Task<Guid> EventIdAsync(Guid instanceId) =>
        (Guid)(await fixture.Tenant.ScalarAsync("SELECT id FROM axis.events WHERE process_instance_id = @instance", ("instance", instanceId)))!;

    private List<WebhookRequest> RequestsOf(string path, Guid eventId) =>
        [.. fixture.Receiver.RequestsTo(path).Where(request => request.Headers["webhook-id"].StartsWith(eventId.ToString("D"), StringComparison.Ordinal))];

    /// <summary>The attempts, the status of the last answer and the number of rows of the event's delivery in <paramref name="state"/>, optionally at one webhook's path.</summary>
    private async Task<(int Attempts, int? Status, long Rows)> DeliveryAsync(Guid eventId, string state, string? path = null)
    {
        Guid? hook = path is null ? null : Assert.Single(fixture.Model.Webhooks, webhook => webhook.Name == (path == WebhookDeliveryFixture.OnePath ? "HookOne" : "HookTwo")).Id;
        await using var command = fixture.Tenant.DataSource.CreateCommand(
            """
            SELECT attempts, status_code FROM axis.webhook_deliveries
            WHERE event_id = @event AND state = @state AND (@hook::uuid IS NULL OR webhook_id = @hook::uuid)
            """);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add(new Npgsql.NpgsqlParameter("hook", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)hook ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = 0L;
        (int, int?) found = default;
        while (await reader.ReadAsync(CancellationToken))
        {
            rows++;
            found = (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1));
        }

        return (found.Item1, found.Item2, rows);
    }

    /// <summary>The Standard Webhooks check: one of the request's signatures is the one of a secret, whose key is the base64 after <c>whsec_</c>, or the secret's own bytes.</summary>
    private static bool VerifiesWithStandardWebhooks(WebhookRequest request, IEnumerable<string> secrets) =>
        secrets.Any(secret => request.Headers["webhook-signature"].Split(' ').Any(signature => string.Equals(signature, Signature(request, secret), StringComparison.Ordinal)));

    private static string Signature(WebhookRequest request, string secret)
    {
        var key = secret.StartsWith("whsec_", StringComparison.Ordinal) ? Convert.FromBase64String(secret["whsec_".Length..]) : Encoding.UTF8.GetBytes(secret);
        var signed = $"{request.Headers["webhook-id"]}.{request.Headers["webhook-timestamp"]}.{request.Body}";
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signed)));
    }
}
