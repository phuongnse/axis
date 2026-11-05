using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Admin;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The publisher checks the pending requests of <c>platform.tenant_host_requests</c> against DNS, here a fake resolver. A record that
/// holds the token adds the host to the tenant, writes the realm's redirect addresses in a real Keycloak and makes the request
/// <c>verified</c>. A request past its seven days becomes <c>expired</c>. A wrong token leaves the request <c>pending</c>. A host
/// that the tenant already holds is not added twice, and a host that another tenant holds stays pending. The certificate of a verified
/// host is written, here by a fake writer, before the request is marked. A failed write leaves the request pending.
/// </summary>
public sealed class TenantHostVerificationTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private const string Token = "verification-token";

    private readonly HttpClientHandler _handler = new();
    private readonly List<string> _realms = [];
    private Uri _baseAddress = null!;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _baseAddress = await keycloak.BaseAddressAsync();

    public async ValueTask DisposeAsync()
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        foreach (var realm in _realms)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{realm}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, CancellationToken);
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _handler.Dispose();
    }

    [Fact]
    public async Task A_record_with_the_token_adds_the_host_to_the_tenant_and_the_realm_and_verifies_the_request()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var host = $"shop-{id}.example.test";
        await RequestAsync(scenario, id, host, expired: false);
        var resolver = new FakeDnsResolver { [$"_axis-verify.{host}"] = [Token] };
        var certificates = new FakeCertificateWriter();

        var result = await RunPublisherUntilAsync(scenario, resolver, certificates, async () => await StateAsync(scenario, id, host) == "verified");

        Assert.Equal(0, result.Exit);
        Assert.Contains($"{id} host verified: {host}", result.Output);
        Assert.Equal([(id, host)], certificates.Writes);
        Assert.Contains(host, await scenario.HostsAsync(id));
        AssertAddresses(await PlatformClientAsync(id), $"{id}.example.test", host);
        var record = Assert.Single(await scenario.AuditRecordsAsync(), record => record.Command == "tenant host verify");
        Assert.Equal(id, record.TenantId);
        Assert.Equal([$"host:{host}"], record.Changed);
        Assert.Equal("operator-1", await TextAsync(scenario, "SELECT actor FROM platform.audit_records WHERE command = 'tenant host verify'"));
        await using var server = AdminScenario.CreateServer(scenario.Platform);
        Assert.NotEqual(HttpStatusCode.NotFound, await StatusAsync(server, host));
    }

    [Fact]
    public async Task A_pending_request_past_its_seven_days_expires_without_a_lookup_and_its_host_answers_404()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var host = $"late-{id}.example.test";
        await RequestAsync(scenario, id, host, expired: true);
        var resolver = new FakeDnsResolver { [$"_axis-verify.{host}"] = [Token] };
        var certificates = new FakeCertificateWriter();

        var result = await RunPublisherUntilAsync(scenario, resolver, certificates, async () => await StateAsync(scenario, id, host) == "expired");

        Assert.Equal(0, result.Exit);
        Assert.Contains($"{id} host expired: {host}", result.Output);
        Assert.Empty(resolver.Names);
        Assert.Empty(certificates.Writes);
        Assert.DoesNotContain(host, await scenario.HostsAsync(id));
        await using var server = AdminScenario.CreateServer(scenario.Platform);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(server, host));
    }

    [Fact]
    public async Task A_record_with_a_wrong_token_or_no_record_or_a_failed_lookup_leaves_the_request_pending_and_its_host_answers_404()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var wrong = $"wrong-{id}.example.test";
        var none = $"none-{id}.example.test";
        var broken = $"broken-{id}.example.test";
        foreach (var host in new[] { wrong, none, broken })
        {
            await RequestAsync(scenario, id, host, expired: false);
        }

        var resolver = new FakeDnsResolver { [$"_axis-verify.{wrong}"] = ["another-token"], FailFor = $"_axis-verify.{broken}" };
        var certificates = new FakeCertificateWriter();

        var result = await RunPublisherUntilAsync(
            scenario,
            resolver,
            certificates,
            async () => await CheckedAsync(scenario, id, wrong) && await CheckedAsync(scenario, id, none) && await CheckedAsync(scenario, id, broken));

        Assert.Equal(0, result.Exit);
        foreach (var host in new[] { wrong, none, broken })
        {
            Assert.Equal("pending", await StateAsync(scenario, id, host));
        }

        Assert.Contains($"{id} host check failed: The lookup failed.", result.Output);
        Assert.Empty(certificates.Writes);
        Assert.Equal([$"{id}.example.test"], await scenario.HostsAsync(id));
        Assert.DoesNotContain(await scenario.AuditRecordsAsync(), record => record.Command == "tenant host verify");
        await using var server = AdminScenario.CreateServer(scenario.Platform);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(server, wrong));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(server, none));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(server, broken));
    }

    [Fact]
    public async Task A_host_that_the_tenant_holds_is_verified_without_a_second_audit_record_and_a_host_of_another_tenant_stays_pending()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var other = await CreateTenantAsync(scenario);
        var held = $"{id}.example.test";
        var taken = $"{other}.example.test";
        await RequestAsync(scenario, id, held, expired: false);
        await RequestAsync(scenario, id, taken, expired: false);
        var resolver = new FakeDnsResolver { [$"_axis-verify.{held}"] = [Token], [$"_axis-verify.{taken}"] = [Token] };
        var certificates = new FakeCertificateWriter();
        var before = (await scenario.AuditRecordsAsync()).Count(record => record.Command == "tenant host verify");

        var result = await RunPublisherUntilAsync(
            scenario,
            resolver,
            certificates,
            async () => await StateAsync(scenario, id, held) == "verified" && await CheckedAsync(scenario, id, taken));

        Assert.Equal(0, result.Exit);
        Assert.Equal([(id, held)], certificates.Writes);
        Assert.Equal(0, before);
        Assert.Equal(0, (await scenario.AuditRecordsAsync()).Count(record => record.Command == "tenant host verify"));
        Assert.Equal("pending", await StateAsync(scenario, id, taken));
        Assert.Equal([held], await scenario.HostsAsync(id));
        Assert.Equal([taken], await scenario.HostsAsync(other));
    }

    [Fact]
    public async Task A_blank_certificate_issuer_still_verifies_the_host_and_writes_no_certificate()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var host = $"plain-{id}.example.test";
        await RequestAsync(scenario, id, host, expired: false);
        var resolver = new FakeDnsResolver { [$"_axis-verify.{host}"] = [Token] };
        var certificates = new FakeCertificateWriter();

        var result = await RunPublisherUntilAsync(
            scenario,
            resolver,
            certificates,
            async () => await StateAsync(scenario, id, host) == "verified",
            issuer: " ");

        Assert.Equal(0, result.Exit);
        Assert.Contains($"{id} host verified: {host}", result.Output);
        Assert.Empty(certificates.Writes);
    }

    [Fact]
    public async Task A_failed_certificate_write_leaves_the_request_pending_and_writes_a_certificate_failed_line()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateTenantAsync(scenario);
        var host = $"cert-{id}.example.test";
        await RequestAsync(scenario, id, host, expired: false);
        var resolver = new FakeDnsResolver { [$"_axis-verify.{host}"] = [Token] };
        var certificates = new FakeCertificateWriter { Fail = true };

        var result = await RunPublisherUntilAsync(
            scenario,
            resolver,
            certificates,
            async () => certificates.Writes.Count > 0 && await CheckedAsync(scenario, id, host));

        Assert.Equal(0, result.Exit);
        Assert.Equal("pending", await StateAsync(scenario, id, host));
        Assert.Contains($"{id} certificate failed: HttpRequestException", result.Output);
        Assert.DoesNotContain($"{id} host verified: {host}", result.Output);
    }

    [Fact]
    public async Task A_certificate_issuer_without_a_service_account_stops_the_publisher_with_exit_2_and_names_the_setting()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST") is not null, "The test host runs in a cluster.");
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var settings = Settings();
        settings["Cell:HostCertificateIssuer"] = "axis-tenant-hosts";

        var result = await scenario.RunAsync(["publisher"], extra: settings, identityProvider: _handler);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        var message = Assert.Single(result.Error);
        Assert.Contains("Cell:HostCertificateIssuer", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("soon")]
    public async Task A_check_interval_that_is_not_a_positive_time_span_stops_the_publisher_with_exit_2_and_names_the_setting(string value)
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var settings = Settings();
        settings["Cell:HostCheckInterval"] = value;

        var result = await scenario.RunAsync(["publisher"], extra: settings, identityProvider: _handler);

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        var message = Assert.Single(result.Error);
        Assert.Contains("Cell:HostCheckInterval", message, StringComparison.Ordinal);
        Assert.DoesNotContain($"'{value}'", message, StringComparison.Ordinal);
    }

    private Dictionary<string, string?> Settings() => new()
    {
        ["Tenancy:Source"] = "directory",
        ["IdentityProvider:Url"] = _baseAddress.ToString(),
        ["IdentityProvider:AdminClientId"] = KeycloakFixture.AdminClientId,
        ["IdentityProvider:AdminClientSecret"] = KeycloakFixture.AdminClientSecret,
    };

    // Creates the tenant with one host and its realm in the real Keycloak.
    private async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        _realms.Add(id);
        var created = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), identityProvider: _handler, extra: Settings());
        Assert.Equal(0, created.Exit);
        return id;
    }

    // Runs the publisher with a one second check interval until the condition holds, then stops it and returns its result.
    private async Task<AdminResult> RunPublisherUntilAsync(
        AdminScenario scenario,
        FakeDnsResolver resolver,
        FakeCertificateWriter certificates,
        Func<Task<bool>> done,
        string issuer = "axis-tenant-hosts")
    {
        var settings = Settings();
        settings["Cell:HostCheckInterval"] = "00:00:01";
        settings["Cell:HostCertificateIssuer"] = issuer;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var run = scenario.RunAsync(["publisher"], stop: stop, extra: settings, identityProvider: _handler, dnsResolver: resolver, certificateWriter: certificates);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
            while (!await done())
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The publisher did not check the host request in time.");
                Assert.False(run.IsCompleted, "The publisher ended before the check.");
                await Task.Delay(200, CancellationToken);
            }
        }
        finally
        {
            await stop.CancelAsync();
        }

        return await run;
    }

    private static async Task RequestAsync(AdminScenario scenario, string tenant, string host, bool expired) =>
        await PostgreSqlServer.AdminScalarAsync(
            scenario.Platform,
            $"""
            INSERT INTO platform.tenant_host_requests (tenant_id, host, requested_at, token, state, expires_at, actor)
            VALUES ('{tenant}', '{host}', now() - interval '8 days', '{Token}', 'pending',
                    now() {(expired ? "- interval '1 day'" : "+ interval '7 days'")}, 'operator-1')
            """,
            CancellationToken);

    private static async Task<string> StateAsync(AdminScenario scenario, string tenant, string host) =>
        await TextAsync(scenario, $"SELECT state FROM platform.tenant_host_requests WHERE tenant_id = '{tenant}' AND host = '{host}'");

    private static async Task<bool> CheckedAsync(AdminScenario scenario, string tenant, string host) =>
        (bool)(await PostgreSqlServer.AdminScalarAsync(
            scenario.Platform,
            $"SELECT checked_at IS NOT NULL FROM platform.tenant_host_requests WHERE tenant_id = '{tenant}' AND host = '{host}'",
            CancellationToken))!;

    private static async Task<string> TextAsync(AdminScenario scenario, string sql) =>
        (string)(await PostgreSqlServer.AdminScalarAsync(scenario.Platform, sql, CancellationToken))!;

    // A server that starts after the check reads the tenant directory as it is then.
    private static async Task<HttpStatusCode> StatusAsync(WebApplicationFactory<Program> server, string host)
    {
        using var client = server.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    // The client holds exactly the addresses of the hosts, in ordinal order.
    private static void AssertAddresses(JsonElement client, params string[] hosts)
    {
        var (redirectUris, webOrigins, postLogout) = RealmSync.PlatformAddresses(hosts);
        string[] Read(string name) => [.. client.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];
        var attribute = RealmClientProfile.ReadAttributes(client).GetValueOrDefault("post.logout.redirect.uris", "");
        Assert.Equal(redirectUris, Read("redirectUris"));
        Assert.Equal(webOrigins, Read("webOrigins"));
        string[] actualPostLogout = [.. attribute.Split("##", StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal)];
        Assert.Equal(postLogout, actualPostLogout);
    }

    // The client as the admin API shows it, read with the master realm's admin client.
    private async Task<JsonElement> PlatformClientAsync(string realm)
    {
        using var http = new HttpClient(_handler, disposeHandler: false);
        var token = await MasterTokenAsync(http);
        var clientId = RealmClientProfile.PlatformClientId;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseAddress, $"admin/realms/{realm}/clients?clientId={clientId}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).EnumerateArray().Single(client => client.GetProperty("clientId").GetString() == clientId);
    }

    private async Task<string> MasterTokenAsync(HttpClient http)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    // Answers from a dictionary of name to values. A name with no entry has no record, and FailFor throws.
    private sealed class FakeDnsResolver : IDnsTxtResolver
    {
        private readonly Dictionary<string, string[]> _records = [];
        private readonly List<string> _names = [];

        public string? FailFor { get; init; }

        public string[] this[string name]
        {
            set => _records[name] = value;
        }

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (_names)
                {
                    return [.. _names];
                }
            }
        }

        public Task<IReadOnlyList<string>> ResolveAsync(string name, CancellationToken cancellationToken)
        {
            lock (_names)
            {
                _names.Add(name);
            }

            if (name == FailFor)
            {
                throw new InvalidOperationException("The lookup failed.");
            }

            return Task.FromResult<IReadOnlyList<string>>(_records.GetValueOrDefault(name) ?? []);
        }
    }

    // Records each write, and throws when Fail is set. The record comes first, so a failed write is counted too.
    private sealed class FakeCertificateWriter : ICertificateWriter
    {
        private readonly List<(string Tenant, string Host)> _writes = [];

        public bool Fail { get; init; }

        public IReadOnlyList<(string Tenant, string Host)> Writes
        {
            get
            {
                lock (_writes)
                {
                    return [.. _writes];
                }
            }
        }

        public Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken)
        {
            lock (_writes)
            {
                _writes.Add((tenantId, host));
            }

            return Fail ? throw new HttpRequestException("The write failed.") : Task.CompletedTask;
        }
    }
}
