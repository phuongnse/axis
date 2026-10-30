using System.Text;
using System.Web;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>OAuthOrder</c> process of StepApp calls a connector with <c>oauth2</c> auth. The stub stands in
/// for the authorization server and for the system. Its token URL and client id and secret are the
/// tenant's <c>PurchasingUrl</c> and <c>PurchasingToken</c> bindings, so the client id and the secret hold the
/// same value. Each test starts with a new worker, so its token cache is empty.
/// </summary>
public sealed class ConnectorOAuth2Tests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private const string Process = "OAuthOrder";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_request_carries_the_client_credentials_in_basic_authentication_with_scope_and_audience_and_the_operation_the_token()
    {
        await fixture.RestartWorkerAsync();
        var tokensBefore = fixture.Stub.TokenRequests.Count;
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, Process);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var tokenRequest = Assert.Single(fixture.Stub.TokenRequests.Skip(tokensBefore));
        var secret = fixture.Token("a");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secret}:{secret}"));
        Assert.Equal(("POST", "/tenant-a", $"Basic {credentials}"), (tokenRequest.Method, tokenRequest.Path, tokenRequest.Authorization));
        var form = HttpUtility.ParseQueryString(tokenRequest.Body);
        Assert.Equal(
            ("client_credentials", "orders.write", "https://purchasing.example.test"),
            (form["grant_type"], form["scope"], form["audience"]));
        Assert.DoesNotContain(secret, tokenRequest.Body, StringComparison.Ordinal);

        Assert.NotNull(tokenRequest.Token);
        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(("POST", "/tenant-a/oauth-orders", $"Bearer {tokenRequest.Token}"), (request.Method, request.Path, request.Authorization));
        Assert.Equal(("succeeded", 1), await CallStateAsync(callId));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Calls_within_the_token_lifetime_share_one_token_and_a_call_after_expires_in_less_a_minute_gets_a_new_one()
    {
        await fixture.RestartWorkerAsync();
        var tokensBefore = fixture.Stub.TokenRequests.Count;
        var lifetime = fixture.Stub.TokenExpiresIn;

        var first = await CallAsync("a");
        var second = await CallAsync("a");
        Assert.Equal(first, second);
        Assert.Equal(1, fixture.Stub.TokenRequests.Count - tokensBefore);

        // One second before the refresh time, the token is still used.
        fixture.Clock.Advance(TimeSpan.FromSeconds(lifetime - 61));
        Assert.Equal(first, await CallAsync("a"));
        Assert.Equal(1, fixture.Stub.TokenRequests.Count - tokensBefore);

        // A second after it, the worker asks for a new token and sends that one.
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var renewed = await CallAsync("a");
        Assert.NotEqual(first, renewed);
        Assert.Equal(2, fixture.Stub.TokenRequests.Count - tokensBefore);
        Assert.Equal(renewed, fixture.Stub.TokenRequests.Last().Token);
    }

    [Fact]
    public async Task A_token_that_lives_a_minute_or_less_is_used_once_and_never_kept()
    {
        await fixture.RestartWorkerAsync();
        var tokensBefore = fixture.Stub.TokenRequests.Count;
        fixture.Stub.TokenExpiresIn = 60;
        try
        {
            var first = await CallAsync("a");
            var second = await CallAsync("a");
            Assert.NotEqual(first, second);
            Assert.Equal(2, fixture.Stub.TokenRequests.Count - tokensBefore);
        }
        finally
        {
            fixture.Stub.TokenExpiresIn = 3600;
        }
    }

    [Fact]
    public async Task Each_tenant_gets_and_sends_its_own_token()
    {
        await fixture.RestartWorkerAsync();
        var tokensBefore = fixture.Stub.TokenRequests.Count;

        var tokenA = await CallAsync("a");
        var tokenB = await CallAsync("b");

        Assert.NotEqual(tokenA, tokenB);
        var requests = fixture.Stub.TokenRequests.Skip(tokensBefore).ToList();
        Assert.Equal(2, requests.Count);
        foreach (var tenant in new[] { "a", "b" })
        {
            var request = Assert.Single(requests, candidate => candidate.Path == $"/tenant-{tenant}");
            var secret = fixture.Token(tenant);
            Assert.Equal($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secret}:{secret}"))}", request.Authorization);
            Assert.Equal(request.Token, tenant == "a" ? tokenA : tokenB);
        }

        // Each tenant's second call reuses its own token, never the other's.
        Assert.Equal(tokenB, await CallAsync("b"));
        Assert.Equal(tokenA, await CallAsync("a"));
        Assert.Equal(2, fixture.Stub.TokenRequests.Count - tokensBefore);
    }

    [Fact]
    public async Task A_refused_token_request_retries_the_call_with_the_status_and_sends_no_operation()
    {
        await fixture.RestartWorkerAsync();
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, Process);
        var callId = await CallIdAsync(instanceId);
        fixture.Stub.RespondToken(500);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        var tenant = fixture.Tenant("a");
        var error = "The token request of the connector 'OAuthPurchasing' answered with the status 500.";
        await using (var command = tenant.DataSource.CreateCommand(
            "SELECT state, attempts, last_status, last_error FROM axis.connector_calls WHERE id = @id"))
        {
            command.Parameters.AddWithValue("id", callId);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));
            Assert.Equal(("pending", 1, 500, error), (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3)));
        }

        await using (var command = tenant.DataSource.CreateCommand(
            "SELECT outcome, http_status, error FROM axis.process_call_attempts WHERE call_id = @id AND kind = 'attempt'"))
        {
            command.Parameters.AddWithValue("id", callId);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));
            Assert.Equal(("retryable", 500, error), (reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
            Assert.False(await reader.ReadAsync(CancellationToken));
        }

        // The failure is not cached, so the retry asks again and sends the operation.
        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal($"Bearer {fixture.Stub.TokenRequests.Last().Token}", request.Authorization);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task No_log_entry_span_tag_audit_record_history_row_call_row_or_attempt_row_holds_the_token_or_the_client_secret()
    {
        await fixture.RestartWorkerAsync();
        var tenant = fixture.Tenant("a");
        var secret = fixture.Token("a");
        var tokensBefore = fixture.Stub.TokenRequests.Count;
        using var collector = new SpanCollector();

        // A refused token request, then a call that succeeds.
        var (_, refused) = await fixture.StageAsync("a", CancellationToken, Process);
        fixture.Stub.RespondToken(401);
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        await fixture.MakeDueAsync("a", await CallIdAsync(refused));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        await CallAsync("a");

        // Both tokens and the secret were in use.
        var issued = fixture.Stub.TokenRequests.Skip(tokensBefore).Select(request => request.Token).OfType<string>().ToList();
        Assert.NotEmpty(issued);
        var secrets = issued.Append(secret).Append(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secret}:{secret}"))).ToList();

        var stored = Assert.IsType<string>(await tenant.ScalarAsync(
            """
            SELECT concat_ws(' ',
                (SELECT string_agg(details::text, ' ') FROM axis.audit_records),
                (SELECT string_agg(concat_ws(' ', input::text, output::text, error), ' ') FROM axis.process_step_history),
                (SELECT string_agg(concat_ws(' ', input::text, last_error), ' ') FROM axis.connector_calls),
                (SELECT string_agg(row_to_json(a)::text, ' ') FROM axis.process_call_attempts a))
            """));
        var spans = string.Join(' ', collector.Spans.SelectMany(span => span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}").Append(span.DisplayName)));
        Assert.NotEmpty(fixture.Logs.Entries);
        Assert.Contains("OAuthPurchasing", stored, StringComparison.Ordinal);
        Assert.Contains("axis.connector.attempt", spans, StringComparison.Ordinal);
        foreach (var value in secrets)
        {
            Assert.DoesNotContain(value, stored, StringComparison.Ordinal);
            Assert.DoesNotContain(value, spans, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(value, StringComparison.Ordinal));
        }
    }

    /// <summary>Stages and runs one call of <paramref name="tenantId"/> and gives the bearer token its operation carried.</summary>
    private async Task<string> CallAsync(string tenantId)
    {
        var (_, instanceId) = await fixture.StageAsync(tenantId, CancellationToken, Process);
        var callId = await CallIdAsync(instanceId, tenantId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(tenantId, CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(tenantId, CancellationToken));
        var authorization = Assert.Single(fixture.Stub.RequestsWithKey(callId)).Authorization;
        Assert.StartsWith("Bearer ", authorization, StringComparison.Ordinal);
        return authorization["Bearer ".Length..];
    }

    private async Task<Guid> CallIdAsync(Guid instanceId, string tenantId = "a") =>
        (Guid)(await fixture.Tenant(tenantId).ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    private async Task<(string State, int Attempts)> CallStateAsync(Guid callId)
    {
        await using var command = fixture.Tenant("a").DataSource.CreateCommand("SELECT state, attempts FROM axis.connector_calls WHERE id = @id");
        command.Parameters.AddWithValue("id", callId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetInt32(1));
    }
}
