using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Axis.Admin;
using Axis.Administration;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The provisioning API of a cell on its console host, with real Keycloak and PostgreSQL. The cell has three tenants and a
/// preview tenant in the platform tables: <c>a</c> is active and served at <c>localhost</c> from a migrated database, <c>b</c>
/// is active, and <c>c</c> is suspended. The operator realm is a realm of its own, made for the test, with the role
/// <c>cell-operator</c> and two service account clients, one with the role and one without. A tenant realm token is a token
/// of the development <c>axis</c> realm. The export routes read and stream a ZIP in a bucket of a real MinIO.
/// </summary>
public sealed class CellProvisioningApiTests(PostgreSqlFixture database, KeycloakFixture keycloak, MinioFixture minio)
    : IClassFixture<PostgreSqlFixture>, IClassFixture<MinioFixture>, IAsyncLifetime
{
    private const string OperatorRole = "cell-operator";
    private const string CallerClientId = "ops-caller";
    private const string NoRoleClientId = "ops-norole";
    private const string ClientSecret = "ops-development-secret";

    private static readonly Uri _console = new("http://console.test/");
    private static readonly Uri _tenantHost = new("http://localhost/");
    private static readonly string[] _createStepNames = ["tenant", "application", "activate", "invite", "resume"];

    private readonly HttpClient _http = new();
    private readonly string _realm = $"ops-{Guid.NewGuid():N}"[..16];
    private Uri _baseAddress = null!;
    private string _platform = null!;
    private CellBackupStorage? _exportStorage;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _baseAddress = await keycloak.BaseAddressAsync();
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, "admin/realms"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        request.Content = JsonContent.Create(new
        {
            realm = _realm,
            enabled = true,
            roles = new { realm = new[] { new { name = OperatorRole } } },
            clients = new[] { CallerClientId, NoRoleClientId }.Select(clientId => new
            {
                clientId,
                enabled = true,
                publicClient = false,
                secret = ClientSecret,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false,
            }),
            users = new[]
            {
                new
                {
                    username = $"service-account-{CallerClientId}",
                    enabled = true,
                    serviceAccountClientId = CallerClientId,
                    realmRoles = new[] { OperatorRole },
                },
                new
                {
                    username = $"service-account-{NoRoleClientId}",
                    enabled = true,
                    serviceAccountClientId = NoRoleClientId,
                    realmRoles = Array.Empty<string>(),
                },
            },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{_realm}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        _http.Dispose();
    }

    [Fact]
    public async Task An_operator_client_with_the_role_reads_the_tenants_and_one_tenant_on_the_console_host()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        // The create job records the application. A tenant that no create job made keeps null.
        await ExecuteAsync("UPDATE platform.tenants SET application = 'approvals' WHERE id = 'a'; UPDATE platform.tenants SET application = 'AcmeCorp' WHERE id = 'b'");
        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/b", token);
        using var suspended = await GetAsync(client, "/api/cell/tenants/c", token);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["a", "b", "c"], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(["active", "active", "suspended"], items.EnumerateArray().Select(item => item.GetProperty("status").GetString()).ToArray());
        Assert.Equal(["approvals", "AcmeCorp", null], items.EnumerateArray().Select(item => item.GetProperty("application").GetString()).ToArray());
        Assert.Equal(["localhost"], Hosts(items[0]));
        Assert.Equal(["b.vn.example.test", "b2.vn.example.test"], Hosts(items[1]));
        Assert.Equal(["c.vn.example.test"], Hosts(items[2]));

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        var tenant = await one.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("b", tenant.GetProperty("id").GetString());
        Assert.Equal("active", tenant.GetProperty("status").GetString());
        Assert.Equal("AcmeCorp", tenant.GetProperty("application").GetString());
        Assert.Equal(["b.vn.example.test", "b2.vn.example.test"], Hosts(tenant));
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        var suspendedTenant = await suspended.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("suspended", suspendedTenant.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, suspendedTenant.GetProperty("application").ValueKind);
    }

    [Fact]
    public async Task The_list_and_the_detail_answer_the_failed_work_counts_of_the_last_sample_and_null_for_a_tenant_without_one()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync(
            """
            INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, failed_instances, unknown_calls, open_dead_letters, sampled_at)
            VALUES ('a', 1, 1, 1, 1, 1, now())
            """);

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var sampled = await GetAsync(client, "/api/cell/tenants/a", token);
        using var unsampled = await GetAsync(client, "/api/cell/tenants/b", token);

        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal((1, 1, 1), FailedWork(items[0]));
        Assert.Null(FailedWork(items[1]));
        Assert.Null(FailedWork(items[2]));
        Assert.Equal((1, 1, 1), FailedWork(await sampled.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)));
        Assert.Null(FailedWork(await unsampled.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)));
    }

    [Fact]
    public async Task The_list_and_the_detail_answer_usage_against_the_limits_and_the_newest_archive_time()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync(
            """
            INSERT INTO platform.tenant_quotas (tenant_id, quota, value, updated_at) VALUES ('a', 'users', 100, now());
            INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at) VALUES ('a', 10, 5, now());
            INSERT INTO platform.tenant_usage (tenant_id, day, users, applications)
            VALUES ('a', (now() AT TIME ZONE 'UTC')::date - 1, 3, 2), ('a', (now() AT TIME ZONE 'UTC')::date, 4, 1);
            INSERT INTO platform.tenant_daily_counters (tenant_id, day, counter, value)
            VALUES ('a', (now() AT TIME ZONE 'UTC')::date, 'emails', 7), ('a', (now() AT TIME ZONE 'UTC')::date - 1, 'emails', 9);
            INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES ('a', '2026-11-03 01:00:00+00')
            """);
        var archive = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero);

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var sampled = await GetAsync(client, "/api/cell/tenants/a", token);
        using var unsampled = await GetAsync(client, "/api/cell/tenants/b", token);

        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        var backedUp = new[] { items[0], await sampled.Content.ReadFromJsonAsync<JsonElement>(CancellationToken) };
        Assert.All(
            backedUp,
            tenant =>
            {
                Assert.Equal((10_737_418_240, 15), Use(tenant, "storage"));
                Assert.Equal((100, 4), Use(tenant, "users"));
                Assert.Equal((10, 1), Use(tenant, "applications"));
                Assert.Equal((1000, 7), Use(tenant, "emailsPerDay"));
                Assert.Equal(archive, tenant.GetProperty("newestArchiveAt").GetDateTimeOffset());
            });
        var bare = new[] { items[1], await unsampled.Content.ReadFromJsonAsync<JsonElement>(CancellationToken) };
        Assert.All(
            bare,
            tenant =>
            {
                Assert.Equal((10_737_418_240, null), Use(tenant, "storage"));
                Assert.Equal((50, null), Use(tenant, "users"));
                Assert.Equal((10, null), Use(tenant, "applications"));
                Assert.Equal((1000, 0), Use(tenant, "emailsPerDay"));
                Assert.Equal(JsonValueKind.Null, tenant.GetProperty("newestArchiveAt").ValueKind);
            });
    }

    [Fact]
    public async Task The_tenant_read_answers_the_days_of_the_last_13_months_newest_first()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync(
            """
            INSERT INTO platform.tenant_usage (tenant_id, day, database_bytes, file_bytes, users, applications, emails_sent, connector_calls, work_items_run)
            VALUES
                ('a', ((now() AT TIME ZONE 'UTC')::date - interval '14 months')::date, 1, 1, 1, 1, 1, 1, 111),
                ('a', ((now() AT TIME ZONE 'UTC')::date - interval '12 months')::date, 20, 21, NULL, NULL, 22, 23, 222),
                ('a', (now() AT TIME ZONE 'UTC')::date - 1, 30, 31, 3, 2, 32, 33, 333)
            """);

        using var tenant = await GetAsync(client, "/api/cell/tenants/a", token);
        using var other = await GetAsync(client, "/api/cell/tenants/b", token);
        using var list = await GetAsync(client, "/api/cell/tenants", token);

        var days = (await tenant.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("days");
        Assert.Equal([333L, 222L], days.EnumerateArray().Select(day => day.GetProperty("workItemsRun").GetInt64()));
        var newest = days[0];
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), newest.GetProperty("day").GetString());
        Assert.Equal((30L, 31L, 3, 2, 32, 33L), (
            newest.GetProperty("databaseBytes").GetInt64(),
            newest.GetProperty("fileBytes").GetInt64(),
            newest.GetProperty("users").GetInt32(),
            newest.GetProperty("applications").GetInt32(),
            newest.GetProperty("emailsSent").GetInt32(),
            newest.GetProperty("connectorCalls").GetInt64()));
        Assert.Equal(JsonValueKind.Null, days[1].GetProperty("users").ValueKind);
        Assert.Equal(0, (await other.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("days").GetArrayLength());
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.All(items.EnumerateArray(), item => Assert.False(item.TryGetProperty("days", out _)));
    }

    [Fact]
    public async Task A_row_written_before_the_counts_existed_answers_null_failed_work()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at) VALUES ('a', 1, 1, now())");

        using var response = await GetAsync(client, "/api/cell/tenants/a", token);

        Assert.Null(FailedWork(await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)));
    }

    [Fact]
    public async Task Every_field_of_the_list_and_the_detail_is_on_the_allowed_list_and_holds_no_secret_binding_or_tenant_user()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        const string bindingUrl = "https://marker-binding.example.test/reports";
        const string userName = "Marker Person";
        const string userEmail = "marker.person@example.test";
        const string requestToken = "marker-request-token-8317";
        await ExecuteAsync(
            $"""
            INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, failed_instances, unknown_calls, open_dead_letters, sampled_at)
            VALUES ('a', 1, 1, 1, 1, 1, now());
            INSERT INTO platform.tenant_usage (tenant_id, day, database_bytes, file_bytes, users, applications, emails_sent, connector_calls, work_items_run)
            VALUES ('a', (now() AT TIME ZONE 'UTC')::date - 1, 1, 1, 1, 1, 1, 1, 1);
            INSERT INTO platform.tenant_backups (tenant_id, last_success_at) VALUES ('a', now());
            INSERT INTO platform.tenant_bindings (tenant_id, name, kind, url) VALUES ('a', 'ReportsUrl', 'url', '{bindingUrl}');
            INSERT INTO platform.tenant_host_requests (tenant_id, host, token, state, requested_at, expires_at, actor)
            VALUES ('a', 'shop.a.example.test', '{requestToken}', 'pending', now(), now() + interval '7 days', 'operator-1')
            """);
        var tenantDatabase = new NpgsqlConnectionStringBuilder(_platform)
        {
            Database = (string)(await ScalarAsync("SELECT database_name FROM platform.tenants WHERE id = 'a'"))!,
        }.ConnectionString;
        await PostgreSqlServer.AdminScalarAsync(
            tenantDatabase,
            $"""
            INSERT INTO axis.user_profiles (user_id, display_name, email, locale, first_signed_in_at, last_signed_in_at)
            VALUES ('marker-user', '{userName}', '{userEmail}', 'en', now(), now())
            """,
            CancellationToken);
        var forbidden = new List<string> { PostgreSqlServer.RolePassword, KeycloakFixture.ClientSecret, ClientSecret, bindingUrl, userName, userEmail, requestToken };
        await using (var connection = new NpgsqlConnection(_platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT runtime_password, schema_password, oidc_client_secret FROM platform.tenants WHERE id IN ('a', 'b', 'c')",
                connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            while (await reader.ReadAsync(CancellationToken))
            {
                for (var column = 0; column < 3; column++)
                {
                    var stored = (byte[])reader.GetValue(column);
                    forbidden.Add(Convert.ToBase64String(stored));
                    forbidden.Add(Convert.ToHexString(stored));
                }
            }
        }

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var detail = await GetAsync(client, "/api/cell/tenants/a", token);

        string[] failedWork = ["failedWork.instances", "failedWork.unknownCalls", "failedWork.deadLetters", "failedWork"];
        string[] usage =
        [
            "usage.storage.limit", "usage.storage.used", "usage.users.limit", "usage.users.used",
            "usage.applications.limit", "usage.applications.used", "usage.emailsPerDay.limit", "usage.emailsPerDay.used",
        ];
        string[] tenantFields = ["id", "status", "application", "hosts[]", .. failedWork, .. usage, "newestArchiveAt", "deleteAfter"];
        AssertFields(
            await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken),
            [.. tenantFields.Select(field => $"items[].{field}")],
            forbidden);
        string[] days = ["day", "databaseBytes", "fileBytes", "users", "applications", "emailsSent", "connectorCalls", "workItemsRun"];
        AssertFields(
            await detail.Content.ReadFromJsonAsync<JsonElement>(CancellationToken),
            [
                .. tenantFields,
                "quotas.*.value",
                "quotas.*.overridden",
                .. days.Select(field => $"days[].{field}"),
                "hostRequests[].host",
                "hostRequests[].state",
                "hostRequests[].expiresAt",
            ],
            forbidden);
    }

    [Fact]
    public async Task An_operator_reads_the_newest_request_of_each_host_ordered_by_host_and_a_caller_without_the_role_is_refused()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        await ExecuteAsync(
            """
            INSERT INTO platform.tenant_host_requests (tenant_id, host, token, state, requested_at, expires_at, actor)
            VALUES
                ('b', 'shop.acme.test', 'token-1', 'expired', now() - interval '20 days', now() - interval '13 days', 'operator-1'),
                ('b', 'shop.acme.test', 'token-2', 'pending', now(), now() + interval '7 days', 'operator-1'),
                ('b', 'portal.acme.test', 'token-3', 'verified', now() - interval '2 days', now() + interval '5 days', 'operator-1'),
                ('b', 'old.acme.test', 'token-4', 'pending', now() - interval '8 days', now() - interval '1 day', 'operator-1'),
                ('c', 'other.acme.test', 'token-5', 'pending', now(), now() + interval '7 days', 'operator-1')
            """);
        var newestExpiry = new DateTimeOffset((DateTime)(await ScalarAsync("SELECT expires_at FROM platform.tenant_host_requests WHERE token = 'token-2'"))!);

        using var response = await GetAsync(client, "/api/cell/tenants/b", token);
        using var noRole = await GetAsync(client, "/api/cell/tenants/b", noRoleToken);
        using var anonymous = await GetAsync(client, "/api/cell/tenants/b", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requests = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("hostRequests");
        Assert.Equal(
            ["old.acme.test/expired", "portal.acme.test/verified", "shop.acme.test/pending"],
            requests.EnumerateArray().Select(item => $"{item.GetProperty("host").GetString()}/{item.GetProperty("state").GetString()}").ToArray());
        Assert.Equal(newestExpiry, requests[2].GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public void The_field_walk_fails_on_a_field_outside_the_allowed_list_and_on_a_planted_value()
    {
        var tenant = JsonSerializer.Deserialize<JsonElement>("""{ "id": "a", "hosts": ["localhost"], "extra": 1 }""");
        var planted = JsonSerializer.Deserialize<JsonElement>("""{ "id": "a", "hosts": ["a.example.test", "x-planted-secret-x"] }""");

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertFields(tenant, ["id", "hosts[]"], []));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertFields(planted, ["id", "hosts[]"], ["planted-secret"]));
        AssertFields(planted, ["id", "hosts[]"], ["not-planted"]);
    }

    [Fact]
    public async Task The_list_leaves_out_a_preview_tenant_and_an_unknown_or_preview_id_answers_404()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var unknown = await GetAsync(client, "/api/cell/tenants/nobody", token);
        using var preview = await GetAsync(client, "/api/cell/tenants/a-preview", token);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
    }

    [Fact]
    public async Task The_list_and_the_detail_answer_the_purge_time_of_a_deleting_tenant_and_null_for_any_other()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("UPDATE platform.tenants SET delete_after = '2026-12-01T10:30:00Z' WHERE id = 'c'");

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var deleting = await GetAsync(client, "/api/cell/tenants/c", token);
        using var active = await GetAsync(client, "/api/cell/tenants/a", token);

        var expected = DateTimeOffset.Parse("2026-12-01T10:30:00Z", CultureInfo.InvariantCulture);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["a", "b", "c"], items.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToArray());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("deleteAfter").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("deleteAfter").ValueKind);
        Assert.Equal("deleting", items[2].GetProperty("status").GetString());
        Assert.Equal(expected, items[2].GetProperty("deleteAfter").GetDateTimeOffset());
        var deletingTenant = await deleting.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(expected, deletingTenant.GetProperty("deleteAfter").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, (await active.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("deleteAfter").ValueKind);
    }

    [Fact]
    public async Task The_deleted_tenant_read_answers_the_purge_time_and_the_completion_7_days_later_and_nothing_of_the_actor_or_hosts()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', '2026-11-01T00:00:00Z', 'operator-1', '{gone.vn.example.test}')");
        await SeedJobAsync("purge", "gone", "succeeded", "[]");

        using var response = await GetAsync(client, "/api/cell/deleted-tenants/gone", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(["id", "deletedAt", "completeAt"], body.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("gone", body.GetProperty("id").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:00:00Z", CultureInfo.InvariantCulture), body.GetProperty("deletedAt").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2026-11-08T00:00:00Z", CultureInfo.InvariantCulture), body.GetProperty("completeAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task The_deleted_tenant_read_answers_404_for_a_preview_tombstone_that_no_purge_job_names_and_for_an_unknown_id()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone-preview', now(), 'operator-1', '{}')");

        using var preview = await GetAsync(client, "/api/cell/deleted-tenants/gone-preview", token);
        using var unknown = await GetAsync(client, "/api/cell/deleted-tenants/nobody", token);
        using var live = await GetAsync(client, "/api/cell/deleted-tenants/a", token);

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal("application/problem+json", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, live.StatusCode);
    }

    [Fact]
    public async Task The_deleted_tenant_read_answers_401_without_a_token_and_403_without_the_role()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', now(), 'operator-1', '{}')");
        await SeedJobAsync("purge", "gone", "succeeded", "[]");

        using var none = await GetAsync(client, "/api/cell/deleted-tenants/gone", token: null);
        using var noRole = await GetAsync(client, "/api/cell/deleted-tenants/gone", noRoleToken);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_token_or_with_a_tenant_realm_token_answers_401()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var tenantRealmToken = await TokenAsync("axis", KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret);

        using var none = await GetAsync(client, "/api/cell/tenants", token: null);
        using var tenantRealm = await GetAsync(client, "/api/cell/tenants", tenantRealmToken);
        using var tenantRealmOne = await GetAsync(client, "/api/cell/tenants/a", tenantRealmToken);
        using var garbage = await GetAsync(client, "/api/cell/tenants", "not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantRealm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantRealmOne.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
    }

    [Fact]
    public async Task An_operator_client_without_the_role_answers_403()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var list = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/a", token);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, one.StatusCode);
    }

    [Fact]
    public async Task Refused_calls_write_one_warning_with_the_route_the_status_the_reason_and_the_caller_and_no_job_or_audit_record()
    {
        var logs = new LogCollector();
        await using var factory = await StartAsync(logs: logs);
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var operatorId = new JsonWebToken(token).Subject;
        using var queued = await PostJobAsync(client, "/api/cell/tenants", token, "key-5093817264", CreateBody("d"));
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var jobs = await CountAsync("platform.jobs");
        var audit = await CountAsync("platform.audit_records");

        var calls = new (Func<Task<HttpResponseMessage>> Send, HttpStatusCode Status, string Route, string Reason)[]
        {
            (() => PostJobAsync(client, "/api/cell/tenants", token, key: null, CreateBody("f")), HttpStatusCode.BadRequest, "/api/cell/tenants", "Idempotency-Key header"),
            (() => PostJobAsync(client, "/api/cell/tenants", token, "key-5093817264", CreateBody("e")), HttpStatusCode.UnprocessableEntity, "/api/cell/tenants", "used before with another request"),
            (() => PostJobAsync(client, "/api/cell/tenants", token, "key-6203948175", CreateBody("BAD-7364519820X")), HttpStatusCode.BadRequest, "/api/cell/tenants", "The tenant id must have"),
            (() => PostJobAsync(client, "/api/cell/tenants/nobody-4829175036/suspend", token, "key-8172635940"), HttpStatusCode.NotFound, "/api/cell/tenants/{id}/suspend", "No tenant has this id."),
        };
        foreach (var (send, status, route, reason) in calls)
        {
            var before = logs.Records.Count(record => record.Message.Contains("by operator", StringComparison.Ordinal));
            using var response = await send();
            Assert.Equal(status, response.StatusCode);
            var written = logs.Records.Where(record => record.Message.Contains("by operator", StringComparison.Ordinal)).Skip(before).ToList();
            var entry = Assert.Single(written);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains($"A POST call to {route} by operator {operatorId} was refused with {(int)status}.", entry.Message, StringComparison.Ordinal);
            Assert.Contains(reason, entry.Message, StringComparison.Ordinal);
        }

        // Every entry of a request carries the framework's RequestPath scope, which holds the path as for any other entry. The
        // warning's own message and every other scope must hold no value of the request.
        var entries = logs.Records.Where(record => record.Message.Contains("by operator", StringComparison.Ordinal)).ToList();
        Assert.All(entries, record => Assert.Contains(record.Scope, pair => pair.Key == "RequestPath"));
        var texts = entries
            .SelectMany(record => record.Scope.Where(pair => pair.Key != "RequestPath").Select(pair => pair.Value?.ToString() ?? "").Append(record.Message))
            .ToList();
        foreach (var absent in new[] { token, "Bearer", "Cookie", "5093817264", "6203948175", "8172635940", "7364519820", "4829175036", "admin@example.test" })
        {
            Assert.All(texts, text => Assert.DoesNotContain(absent, text, StringComparison.Ordinal));
        }

        Assert.Equal(jobs, await CountAsync("platform.jobs"));
        Assert.Equal(audit, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task An_anonymous_call_writes_no_refused_entry_and_a_caller_without_the_role_writes_only_the_403_warning()
    {
        var logs = new LogCollector();
        await using var factory = await StartAsync(logs: logs);
        using var client = CreateClient(factory, _console);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var anonymous = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token: null, "key-3958172640");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("refused", StringComparison.Ordinal));

        using var noRole = await PostJobAsync(client, "/api/cell/tenants/b/suspend", noRoleToken, "key-3958172641");
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        var refused = Assert.Single(logs.Entries, entry => entry.Contains("refused", StringComparison.Ordinal));
        Assert.Contains("refused with 403", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("by operator", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_administrators_route_lists_a_suspended_tenants_enabled_administrators_with_email_name_and_language()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var tenant = $"adm-{Guid.NewGuid():N}"[..16];
        const string usersSecret = "users-secret-1";
        var admin = await TokenAsync("master", KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret);
        await new TenantRealm(_http, new IdentityProviderAdmin(_baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
            .EnsureAsync(tenant, "Acme", new TenantRealmSecrets("platform-secret-1", "activation-secret-1", usersSecret, "agent-secret-1"), new Dictionary<string, string>(), CancellationToken);
        try
        {
            var tenantDatabase = await CellTestPlatform.AddRealmTenantAsync(database, _platform, tenant, "suspended", _baseAddress, usersSecret, CancellationToken);
            var enabledAdministrator = await CreateRealmUserAsync(admin, tenant, "ada@example.test", "Ada", "Lovelace", true, "vi");
            var disabledAdministrator = await CreateRealmUserAsync(admin, tenant, "old@example.test", "Old", "Admin", false, "en");
            await CreateRealmUserAsync(admin, tenant, "member@example.test", "Mem", "Ber", true, "en");
            await using (var connection = new NpgsqlConnection(tenantDatabase))
            {
                await connection.OpenAsync(CancellationToken);
                foreach (var holder in new[] { enabledAdministrator, disabledAdministrator })
                {
                    await using var command = new NpgsqlCommand(
                        "INSERT INTO axis.given_roles (id, user_id, application_id, role) VALUES (gen_random_uuid(), @user, NULL, 'administrator')", connection);
                    command.Parameters.AddWithValue("user", holder);
                    await command.ExecuteNonQueryAsync(CancellationToken);
                }
            }

            using var response = await GetAsync(client, $"/api/cell/tenants/{tenant}/administrators", token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").EnumerateArray().ToList();
            var item = Assert.Single(items);
            Assert.Equal("ada@example.test", item.GetProperty("email").GetString());
            Assert.Equal("Ada Lovelace", item.GetProperty("displayName").GetString());
            Assert.Equal("vi", item.GetProperty("language").GetString());
            Assert.Equal(["email", "displayName", "language"], item.EnumerateObject().Select(property => property.Name).ToArray());
        }
        finally
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(_baseAddress, $"admin/realms/{tenant}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
            using var deleted = await _http.SendAsync(request, CancellationToken);
        }
    }

    [Fact]
    public async Task The_administrators_route_answers_401_without_a_token_403_without_the_role_and_404_for_an_unknown_or_a_preview_tenant()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var none = await GetAsync(client, "/api/cell/tenants/c/administrators", token: null);
        using var noRole = await GetAsync(client, "/api/cell/tenants/c/administrators", noRoleToken);
        using var unknown = await GetAsync(client, "/api/cell/tenants/zz-unknown/administrators", operatorToken);
        using var preview = await GetAsync(client, "/api/cell/tenants/a-preview/administrators", operatorToken);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
    }

    [Fact]
    public async Task The_console_host_serves_the_probes_and_answers_404_for_tenant_routes_and_unknown_cell_routes()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var live = await GetAsync(client, "/health/live", token: null);
        using var me = await GetAsync(client, "/api/me", token: null);
        using var meWithToken = await GetAsync(client, "/api/me", token);
        using var other = await GetAsync(client, "/api/cell/other", token);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, meWithToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task A_tenant_host_answers_404_for_the_cell_routes_before_any_sign_in_work()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _tenantHost);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var none = await GetAsync(client, "/api/cell/tenants", token: null);
        using var operatorToken = await GetAsync(client, "/api/cell/tenants", token);
        using var one = await GetAsync(client, "/api/cell/tenants/a", token);
        using var me = await GetAsync(client, "/api/me", token: null);

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, operatorToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, one.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task An_operator_sets_and_clears_quotas_and_the_tenant_read_and_an_audit_record_naming_the_caller_show_them()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var set = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = 100, ["storage"] = null } });
        using var afterSet = await GetAsync(client, "/api/cell/tenants/b", token);
        using var remove = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { remove = new List<string> { "users" } });
        using var afterRemove = await GetAsync(client, "/api/cell/tenants/b", token);

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var setQuotas = (await set.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(setQuotas, "users", 100, true);
        AssertQuota(setQuotas, "storage", null, true);
        AssertQuota(setQuotas, "applications", 10, false);
        var readQuotas = (await afterSet.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(readQuotas, "users", 100, true);
        AssertQuota(readQuotas, "storage", null, true);
        AssertQuota(readQuotas, "applications", 10, false);
        AssertQuota(readQuotas, "authoringRequests", null, false);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        AssertQuota((await remove.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas"), "users", 50, false);
        var removedQuotas = (await afterRemove.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("quotas");
        AssertQuota(removedQuotas, "users", 50, false);
        AssertQuota(removedQuotas, "storage", null, true);

        var records = await AuditRecordsAsync();
        Assert.Equal(
            [("tenant quotas set", "b", "storage,users"), ("tenant quotas set", "b", "users")],
            records);
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var actors = new NpgsqlCommand("SELECT actor FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await actors.ExecuteReaderAsync(CancellationToken);
        var subject = new JsonWebToken(token).Subject;
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(subject, reader.GetString(0));
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal(subject, reader.GetString(0));
    }

    [Fact]
    public async Task An_unknown_key_or_a_negative_value_answers_400_and_writes_nothing()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var unknown = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["nope"] = 1 } });
        using var negative = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = -1 } });
        using var unknownRemoved = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { remove = new List<string> { "nope" } });
        using var both = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { set = new Dictionary<string, long?> { ["users"] = 1 }, remove = new List<string> { "users" } });
        using var empty = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token, new { });

        foreach (var response in new[] { unknown, negative, unknownRemoved, both, empty })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(0, await CountAsync("platform.tenant_quotas"));
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task Setting_quotas_without_the_role_answers_403_and_an_unknown_or_preview_tenant_answers_404()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        var body = new { set = new Dictionary<string, long?> { ["users"] = 1 } };

        using var noRole = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", noRoleToken, body);
        using var none = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/b/quotas", token: null, body);
        using var unknown = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/nobody/quotas", operatorToken, body);
        using var preview = await SendAsync(client, HttpMethod.Put, "/api/cell/tenants/a-preview/quotas", operatorToken, body);

        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(0, await CountAsync("platform.tenant_quotas"));
        Assert.Equal(0, await CountAsync("platform.audit_records"));
    }

    [Fact]
    public async Task A_host_request_answers_201_with_the_token_the_txt_record_and_the_cname_and_stores_a_pending_row()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var response = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = " Shop.Acme.Test " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var requestToken = body.GetProperty("token").GetString()!;
        Assert.Equal(43, requestToken.Length);
        Assert.Equal("shop.acme.test", body.GetProperty("host").GetString());
        Assert.Equal("pending", body.GetProperty("state").GetString());
        Assert.InRange(body.GetProperty("expiresAt").GetDateTimeOffset(), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(5));
        Assert.Equal("_axis-verify.shop.acme.test", body.GetProperty("txt").GetProperty("name").GetString());
        Assert.Equal(requestToken, body.GetProperty("txt").GetProperty("value").GetString());
        Assert.Equal("shop.acme.test", body.GetProperty("cname").GetProperty("name").GetString());
        Assert.Equal("b.vn.example.test", body.GetProperty("cname").GetProperty("target").GetString());

        Assert.Equal(1, await CountAsync("platform.tenant_host_requests"));
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id, host, token, state, actor, checked_at IS NULL FROM platform.tenant_host_requests", connection);
        await using var rows = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await rows.ReadAsync(CancellationToken));
        Assert.Equal(
            ("b", "shop.acme.test", requestToken, "pending", new JsonWebToken(token).Subject, true),
            (rows.GetString(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), rows.GetBoolean(5)));
    }

    [Fact]
    public async Task A_repeat_request_of_the_same_tenant_answers_200_with_the_same_token_and_another_tenant_may_request_the_host()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "shop.acme.test" });
        using var again = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "SHOP.acme.test" });
        using var other = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/c/hosts", token, new { host = "shop.acme.test" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        var firstToken = (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("token").GetString();
        var againBody = await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var otherBody = await other.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(firstToken, againBody.GetProperty("token").GetString());
        Assert.NotEqual(firstToken, otherBody.GetProperty("token").GetString());
        Assert.Equal("c.vn.example.test", otherBody.GetProperty("cname").GetProperty("target").GetString());
        Assert.Equal(2, await CountAsync("platform.tenant_host_requests"));
    }

    [Fact]
    public async Task A_request_after_the_pending_one_expired_stores_a_new_row_with_a_new_token()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "shop.acme.test" });
        await ExecuteAsync("UPDATE platform.tenant_host_requests SET expires_at = now() - interval '1 minute'");
        using var second = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "shop.acme.test" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual(
            (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("token").GetString(),
            (await second.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("token").GetString());
        Assert.Equal(2, await CountAsync("platform.tenant_host_requests"));
    }

    [Fact]
    public async Task A_host_that_another_tenant_holds_answers_409_without_naming_the_tenant()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var response = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/c/hosts", token, new { host = "B2.vn.example.test" });
        using var own = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "b2.vn.example.test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("\"b\"", body);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal(0, await CountAsync("platform.tenant_host_requests"));
    }

    [Fact]
    public async Task A_requested_host_that_is_not_verified_still_answers_404_on_a_tenant_route()
    {
        await using var factory = await StartAsync();
        using var console = CreateClient(factory, _console);
        using var requested = CreateClient(factory, new Uri("http://shop.acme.test/"));
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var request = await SendAsync(console, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "shop.acme.test" });
        using var root = await GetAsync(requested, "/", token: null);
        using var me = await GetAsync(requested, "/api/me", token: null);

        Assert.Equal(HttpStatusCode.Created, request.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, me.StatusCode);
    }

    [Fact]
    public async Task A_host_request_without_the_role_answers_403_without_a_token_401_and_stores_nothing()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        var body = new { host = "shop.acme.test" };

        using var noRole = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", noRoleToken, body);
        using var none = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token: null, body);

        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(0, await CountAsync("platform.tenant_host_requests"));
    }

    [Fact]
    public async Task A_blank_or_malformed_host_answers_400_and_an_unknown_or_preview_tenant_answers_404()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        var bad = new List<HttpResponseMessage>();
        try
        {
            foreach (var host in new[] { "", "  ", "not a host", "localhost", "1.2.3.4", "shop.acme.test.", "-a.acme.test", "a_b.acme.test", "a..acme.test", new string('a', 64) + ".test" })
            {
                bad.Add(await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host }));
            }

            bad.Add(await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { }));
            using var unknown = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/nobody/hosts", token, new { host = "shop.acme.test" });
            using var preview = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/a-preview/hosts", token, new { host = "shop.acme.test" });

            Assert.All(bad, response =>
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            });
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(0, await CountAsync("platform.tenant_host_requests"));
        }
        finally
        {
            foreach (var response in bad)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Suspend_and_resume_queue_jobs_that_the_runner_finishes_with_one_audit_record_naming_the_operator()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var suspend = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "suspend-b");
        Assert.Equal(HttpStatusCode.Accepted, suspend.StatusCode);
        var suspendJob = await suspend.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("suspend", suspendJob.GetProperty("kind").GetString());
        Assert.Equal("b", suspendJob.GetProperty("tenantId").GetString());
        Assert.Equal("queued", suspendJob.GetProperty("state").GetString());
        var suspendId = suspendJob.GetProperty("id").GetGuid();
        Assert.Equal($"/api/cell/jobs/{suspendId:D}", suspend.Headers.Location?.OriginalString);
        Assert.True(await RunJobAsync());

        using var resume = await PostJobAsync(client, "/api/cell/tenants/c/resume", token, "resume-c");
        Assert.Equal(HttpStatusCode.Accepted, resume.StatusCode);
        var resumeId = (await resume.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        Assert.True(await RunJobAsync());

        using var tenantB = await GetAsync(client, "/api/cell/tenants/b", token);
        using var tenantC = await GetAsync(client, "/api/cell/tenants/c", token);
        Assert.Equal("suspended", (await tenantB.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());
        Assert.Equal("active", (await tenantC.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());

        var operatorId = new JsonWebToken(token).Subject;
        foreach (var (id, kind, tenant) in new[] { (suspendId, "suspend", "b"), (resumeId, "resume", "c") })
        {
            using var read = await GetAsync(client, $"/api/cell/jobs/{id:D}", token);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal(kind, job.GetProperty("kind").GetString());
            Assert.Equal(tenant, job.GetProperty("tenantId").GetString());
            Assert.Equal("succeeded", job.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, job.GetProperty("error").ValueKind);
            var step = Assert.Single(job.GetProperty("steps").EnumerateArray());
            Assert.Equal("status", step.GetProperty("name").GetString());
            Assert.Equal("succeeded", step.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, step.GetProperty("error").ValueKind);

            await using var connection = new NpgsqlConnection(_platform);
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT command, tenant_id, actor FROM platform.audit_records WHERE changed = ARRAY[@changed]", connection);
            command.Parameters.AddWithValue("changed", $"job:{id:D}");
            await using var records = await command.ExecuteReaderAsync(CancellationToken);
            Assert.True(await records.ReadAsync(CancellationToken));
            Assert.Equal(($"tenant {kind}", tenant, operatorId), (records.GetString(0), records.GetString(1), records.GetString(2)));
            Assert.False(await records.ReadAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task Read_only_queues_a_job_that_the_runner_finishes_with_one_audit_record_naming_the_operator_and_a_caller_without_the_role_gets_403_and_no_job()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var none = await PostJobAsync(client, "/api/cell/tenants/b/read-only", token: null, "read-only-none");
        using var noRole = await PostJobAsync(client, "/api/cell/tenants/b/read-only", noRoleToken, "read-only-b");
        using var preview = await PostJobAsync(client, "/api/cell/tenants/a-preview/read-only", token, "read-only-preview");
        using var unknown = await PostJobAsync(client, "/api/cell/tenants/nobody/read-only", token, "read-only-nobody");
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));

        using var readOnly = await PostJobAsync(client, "/api/cell/tenants/b/read-only", token, "read-only-b");
        Assert.Equal(HttpStatusCode.Accepted, readOnly.StatusCode);
        var queued = await readOnly.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("read-only", queued.GetProperty("kind").GetString());
        Assert.Equal("b", queued.GetProperty("tenantId").GetString());
        Assert.Equal("queued", queued.GetProperty("state").GetString());
        var jobId = queued.GetProperty("id").GetGuid();
        Assert.True(await RunJobAsync());

        using var tenant = await GetAsync(client, "/api/cell/tenants/b", token);
        Assert.Equal("readOnly", (await tenant.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());
        using var read = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", token);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        Assert.Equal("status", Assert.Single(job.GetProperty("steps").EnumerateArray()).GetProperty("name").GetString());

        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, actor FROM platform.audit_records WHERE changed = ARRAY[@changed]", connection);
        command.Parameters.AddWithValue("changed", $"job:{jobId:D}");
        await using var records = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await records.ReadAsync(CancellationToken));
        Assert.Equal(("tenant read-only", "b", new JsonWebToken(token).Subject), (records.GetString(0), records.GetString(1), records.GetString(2)));
        Assert.False(await records.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task Delete_queues_a_job_that_suspends_the_tenant_and_its_preview_and_sets_the_deletion_date_7_days_ahead_and_the_reads_answer_deleting()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var delete = await PostJobAsync(client, "/api/cell/tenants/a", token, "delete-a", method: HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        var queued = await delete.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("delete", queued.GetProperty("kind").GetString());
        Assert.Equal("a", queued.GetProperty("tenantId").GetString());
        var jobId = queued.GetProperty("id").GetGuid();
        Assert.True(await RunJobAsync());

        using var read = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", token);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        var step = Assert.Single(job.GetProperty("steps").EnumerateArray());
        Assert.Equal(("schedule", "succeeded"), (step.GetProperty("name").GetString(), step.GetProperty("state").GetString()));

        foreach (var id in new[] { "a", "a-preview" })
        {
            Assert.Equal("suspended", await ScalarAsync($"SELECT status FROM platform.tenants WHERE id = '{id}'"));
            var deleteAfter = (DateTime)(await ScalarAsync($"SELECT delete_after FROM platform.tenants WHERE id = '{id}'"))!;
            Assert.InRange(deleteAfter, DateTime.UtcNow.AddDays(7).AddMinutes(-5), DateTime.UtcNow.AddDays(7).AddMinutes(5));
        }

        using var one = await GetAsync(client, "/api/cell/tenants/a", token);
        using var list = await GetAsync(client, "/api/cell/tenants", token);
        Assert.Equal("deleting", (await one.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(["deleting", "active", "suspended"], items.EnumerateArray().Select(item => item.GetProperty("status").GetString()).ToArray());
    }

    [Fact]
    public async Task A_second_delete_job_keeps_the_first_deletion_date()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await PostJobAsync(client, "/api/cell/tenants/a", token, "delete-1", method: HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.True(await RunJobAsync());
        var date = await ScalarAsync("SELECT delete_after FROM platform.tenants WHERE id = 'a'");
        await ExecuteAsync("UPDATE platform.tenants SET delete_after = delete_after - interval '3 days' WHERE id IN ('a', 'a-preview')");
        var earlier = await ScalarAsync("SELECT delete_after FROM platform.tenants WHERE id = 'a'");

        using var second = await PostJobAsync(client, "/api/cell/tenants/a", token, "delete-2", method: HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.True(await RunJobAsync());

        Assert.NotEqual(date, earlier);
        Assert.Equal(earlier, await ScalarAsync("SELECT delete_after FROM platform.tenants WHERE id = 'a'"));
        Assert.Equal(earlier, await ScalarAsync("SELECT delete_after FROM platform.tenants WHERE id = 'a-preview'"));
    }

    [Fact]
    public async Task A_cancel_within_the_window_clears_the_deletion_date_and_leaves_the_tenant_suspended_with_its_database_and_hosts()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        const string Snapshot =
            """
            SELECT (SELECT database_name FROM platform.tenants WHERE id = 'a') || '|'
              || (SELECT count(*) FROM pg_database WHERE datname = (SELECT database_name FROM platform.tenants WHERE id = 'a')) || '|'
              || (SELECT string_agg(host, ',' ORDER BY host) FROM platform.tenant_hosts WHERE tenant_id = 'a') || '|'
              || (SELECT count(*) FROM platform.tenants WHERE id IN ('a', 'a-preview'))
            """;
        var before = await ScalarAsync(Snapshot);

        using var delete = await PostJobAsync(client, "/api/cell/tenants/a", token, "delete-a", method: HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.Accepted, delete.StatusCode);
        Assert.True(await RunJobAsync());
        using var cancel = await PostJobAsync(client, "/api/cell/tenants/a/deletion/cancel", token, "cancel-a");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var queued = await cancel.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("cancel-deletion", queued.GetProperty("kind").GetString());
        Assert.True(await RunJobAsync());

        using var read = await GetAsync(client, $"/api/cell/jobs/{queued.GetProperty("id").GetGuid():D}", token);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("succeeded", job.GetProperty("state").GetString());
        Assert.Equal("cancel", Assert.Single(job.GetProperty("steps").EnumerateArray()).GetProperty("name").GetString());
        foreach (var id in new[] { "a", "a-preview" })
        {
            Assert.Equal(DBNull.Value, await ScalarAsync($"SELECT delete_after FROM platform.tenants WHERE id = '{id}'"));
            Assert.Equal("suspended", await ScalarAsync($"SELECT status FROM platform.tenants WHERE id = '{id}'"));
        }

        using var one = await GetAsync(client, "/api/cell/tenants/a", token);
        Assert.Equal("suspended", (await one.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString());
        Assert.Equal(before, await ScalarAsync(Snapshot));
    }

    [Fact]
    public async Task A_cancel_after_the_deletion_date_fails_its_job_and_the_date_stays()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        using var delete = await PostJobAsync(client, "/api/cell/tenants/a", token, "delete-a", method: HttpMethod.Delete);
        Assert.True(await RunJobAsync());
        await ExecuteAsync("UPDATE platform.tenants SET delete_after = now() - interval '1 minute' WHERE id IN ('a', 'a-preview')");

        using var cancel = await PostJobAsync(client, "/api/cell/tenants/a/deletion/cancel", token, "cancel-a");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var jobId = (await cancel.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        Assert.True(await RunJobAsync());

        using var read = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", token);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("failed", job.GetProperty("state").GetString());
        Assert.Equal("The grace window of the deletion has ended.", job.GetProperty("error").GetString());
        foreach (var id in new[] { "a", "a-preview" })
        {
            Assert.NotEqual(DBNull.Value, await ScalarAsync($"SELECT delete_after FROM platform.tenants WHERE id = '{id}'"));
        }
    }

    [Fact]
    public async Task A_cancel_for_a_tenant_with_no_scheduled_deletion_succeeds_and_changes_nothing()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var cancel = await PostJobAsync(client, "/api/cell/tenants/b/deletion/cancel", token, "cancel-b");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var jobId = (await cancel.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        Assert.True(await RunJobAsync());

        using var read = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", token);
        Assert.Equal("succeeded", (await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("state").GetString());
        Assert.Equal("active", await ScalarAsync("SELECT status FROM platform.tenants WHERE id = 'b'"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT delete_after FROM platform.tenants WHERE id = 'b'"));
    }

    [Fact]
    public async Task The_delete_and_cancel_routes_answer_401_without_a_token_403_without_the_role_and_404_for_a_preview_or_unknown_id_and_queue_nothing()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        foreach (var (method, suffix) in new[] { (HttpMethod.Delete, ""), (HttpMethod.Post, "/deletion/cancel") })
        {
            using var none = await PostJobAsync(client, $"/api/cell/tenants/b{suffix}", token: null, "k", method: method);
            using var noRole = await PostJobAsync(client, $"/api/cell/tenants/b{suffix}", noRoleToken, "k", method: method);
            using var preview = await PostJobAsync(client, $"/api/cell/tenants/a-preview{suffix}", operatorToken, "k", method: method);
            using var unknown = await PostJobAsync(client, $"/api/cell/tenants/nobody{suffix}", operatorToken, "k", method: method);
            using var noKey = await PostJobAsync(client, $"/api/cell/tenants/b{suffix}", operatorToken, key: null, method: method);

            Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        }

        Assert.Equal(0, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task An_operator_queues_an_export_job_with_one_pending_step_and_an_unknown_tenant_a_preview_tenant_and_a_missing_key_are_refused()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var unknown = await PostJobAsync(client, "/api/cell/tenants/nobody/exports", token, "export-nobody");
        using var preview = await PostJobAsync(client, "/api/cell/tenants/a-preview/exports", token, "export-preview");
        using var noKey = await PostJobAsync(client, "/api/cell/tenants/b/exports", token, key: null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));

        using var export = await PostJobAsync(client, "/api/cell/tenants/b/exports", token, "export-b");
        using var again = await PostJobAsync(client, "/api/cell/tenants/b/exports", token, "export-b");
        using var otherKind = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "export-b");

        Assert.Equal(HttpStatusCode.Accepted, export.StatusCode);
        var queued = await export.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("export", queued.GetProperty("kind").GetString());
        Assert.Equal("b", queued.GetProperty("tenantId").GetString());
        Assert.Equal("queued", queued.GetProperty("state").GetString());
        var id = queued.GetProperty("id").GetGuid();
        Assert.Equal($"/api/cell/jobs/{id:D}", export.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(id, (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, otherKind.StatusCode);
        Assert.Equal(1, await CountAsync("platform.jobs"));

        using var read = await GetAsync(client, $"/api/cell/jobs/{id:D}", token);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var step = Assert.Single(job.GetProperty("steps").EnumerateArray());
        Assert.Equal("export", step.GetProperty("name").GetString());
        Assert.Equal("pending", step.GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_same_key_and_body_answer_the_first_job_and_any_other_request_with_that_key_answers_422()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "key-1", "{}");
        using var again = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "key-1", "{}");
        using var otherBody = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "key-1", "{\"x\":1}");
        using var otherKind = await PostJobAsync(client, "/api/cell/tenants/b/resume", token, "key-1", "{}");
        using var otherTenant = await PostJobAsync(client, "/api/cell/tenants/c/suspend", token, "key-1", "{}");
        using var noKey = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, key: null);
        using var longKey = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, new string('k', 256));
        using var blankKey = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, " ");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid(),
            (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        foreach (var response in new[] { otherBody, otherKind, otherTenant })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        foreach (var response in new[] { noKey, longKey, blankKey })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task Parallel_retries_with_one_key_queue_one_job()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "parallel")));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            Assert.Equal(1, await CountAsync("platform.jobs"));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_key_whose_job_is_older_than_24_hours_queues_a_new_job()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "old-key", "{}");
        await using (var connection = new NpgsqlConnection(_platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var age = new NpgsqlCommand("UPDATE platform.jobs SET created_at = now() - interval '25 hours'", connection);
            await age.ExecuteNonQueryAsync(CancellationToken);
        }

        using var second = await PostJobAsync(client, "/api/cell/tenants/b/suspend", token, "old-key", "{}");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.NotEqual(
            (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid(),
            (await second.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        Assert.Equal(2, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task Running_a_failed_create_job_again_queues_a_job_that_keeps_the_steps_done_and_the_publisher_runs_only_the_rest()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var failed = await SeedJobAsync("create", "b", "failed", CreateSteps("succeeded", "succeeded", "succeeded", "failed", "pending"), """{"administrator":"admin@example.test"}""");

        using var response = await PostJobAsync(client, $"/api/cell/jobs/{failed:D}/run-again", token, "again-1");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var id = accepted.GetProperty("id").GetGuid();
        Assert.NotEqual(failed, id);
        Assert.Equal(("create", "b", "queued"), (accepted.GetProperty("kind").GetString(), accepted.GetProperty("tenantId").GetString(), accepted.GetProperty("state").GetString()));
        Assert.Equal($"/api/cell/jobs/{id:D}", response.Headers.Location?.OriginalString);
        Assert.Equal(2, await CountAsync("platform.jobs"));
        Assert.Equal("failed", await ScalarAsync($"SELECT state FROM platform.jobs WHERE id = '{failed:D}'"));
        Assert.Equal("admin@example.test", await ScalarAsync($"SELECT input ->> 'administrator' FROM platform.jobs WHERE id = '{id:D}'"));
        Assert.Equal(0, await ScalarAsync($"SELECT claims FROM platform.jobs WHERE id = '{id:D}'"));
        using var read = await GetAsync(client, $"/api/cell/jobs/{id:D}", token);
        var steps = (await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("steps").EnumerateArray()
            .Select(step => $"{step.GetProperty("name").GetString()}={step.GetProperty("state").GetString()}:{step.GetProperty("error").ValueKind}").ToArray();
        Assert.Equal(["tenant=succeeded:Null", "application=succeeded:Null", "activate=succeeded:Null", "invite=pending:Null", "resume=pending:Null"], steps);

        var ran = new List<string>();
        var kinds = new Dictionary<string, IReadOnlyList<JobStep>>
        {
            ["create"] = [.. _createStepNames
                .Select(name => new JobStep(name, (_, _, _, _) => { ran.Add(name); return Task.FromResult<string?>(null); }))],
        };
        Assert.True(await new ProvisioningJobs(_platform, kinds, TimeSpan.FromMinutes(5)).RunNextAsync(TextWriter.Null, CancellationToken));

        Assert.Equal(["invite", "resume"], ran);
        Assert.Equal("succeeded", await ScalarAsync($"SELECT state FROM platform.jobs WHERE id = '{id:D}'"));
        var records = await AuditRecordsAsync();
        var record = Assert.Single(records);
        Assert.Equal(("tenant create", "b", $"job:{id:D}"), record);
        Assert.Equal(new JsonWebToken(token).Subject, await ScalarAsync("SELECT actor FROM platform.audit_records"));
    }

    [Fact]
    public async Task Running_a_job_again_that_is_not_failed_or_is_a_purge_or_has_a_newer_job_answers_409_and_queues_nothing()
    {
        await using var factory = await StartAsync(tenantCap: 4);
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', now(), 'operator-1', '{}')");
        var statusStep = """[{"name":"status","state":"failed","error":"The status could not change."}]""";
        var queued = await SeedJobAsync("suspend", "b", "queued", statusStep);
        var running = await SeedJobAsync("resume", "c", "running", statusStep);
        var succeeded = await SeedJobAsync("read-only", "b", "succeeded", statusStep);
        var purge = await SeedJobAsync("purge", "c", "failed", statusStep);
        var superseded = await SeedJobAsync("export", "b", "failed", statusStep, createdAgo: TimeSpan.FromMinutes(10));
        await SeedJobAsync("export", "b", "succeeded", statusStep);
        var deleted = await SeedJobAsync("create", "gone", "failed", CreateSteps("pending", "pending", "pending", "pending", "pending"));
        var full = await SeedJobAsync("create", "d", "failed", CreateSteps("pending", "pending", "pending", "pending", "pending"));
        var before = await CountAsync("platform.jobs");

        var answers = new List<(string Name, HttpStatusCode Status, string? Title)>();
        foreach (var (name, id) in new[] { ("queued", queued), ("running", running), ("succeeded", succeeded), ("purge", purge), ("superseded", superseded), ("deleted", deleted), ("full", full) })
        {
            using var response = await PostJobAsync(client, $"/api/cell/jobs/{id:D}/run-again", token, $"refused-{name}");
            answers.Add((name, response.StatusCode, (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("title").GetString()));
        }

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.Conflict, answer.Status));
        Assert.Equal(
            [
                "Only a failed job can be run again.",
                "Only a failed job can be run again.",
                "Only a failed job can be run again.",
                "The publisher queues a failed purge job again on its own.",
                "The tenant has a newer job of this kind.",
                "A tenant of the cell already has this id.",
                "This cell is full.",
            ],
            answers.Select(answer => answer.Title));
        Assert.Equal(before, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task Running_a_failed_create_job_again_for_a_tenant_with_a_directory_row_is_allowed_in_a_cell_at_its_cap()
    {
        await using var factory = await StartAsync(tenantCap: 4);
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var failed = await SeedJobAsync("create", "b", "failed", CreateSteps("succeeded", "succeeded", "succeeded", "failed", "pending"));

        using var response = await PostJobAsync(client, $"/api/cell/jobs/{failed:D}/run-again", token, "again-b");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Running_a_job_again_answers_401_without_a_token_403_without_the_role_404_for_an_unknown_job_400_without_a_key_and_replays_the_first_job_for_one_key()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);
        var failed = await SeedJobAsync("create", "b", "failed", CreateSteps("succeeded", "failed", "pending", "pending", "pending"));
        var path = $"/api/cell/jobs/{failed:D}/run-again";

        using var none = await PostJobAsync(client, path, token: null, "again-none");
        using var noRole = await PostJobAsync(client, path, noRoleToken, "again-norole");
        using var unknown = await PostJobAsync(client, $"/api/cell/jobs/{Guid.NewGuid():D}/run-again", token, "again-unknown");
        using var withoutKey = await PostJobAsync(client, path, token, key: null);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal(1, await CountAsync("platform.jobs"));

        using var first = await PostJobAsync(client, path, token, "again-replay");
        using var replay = await PostJobAsync(client, path, token, "again-replay");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid(),
            (await replay.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        Assert.Equal(2, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task An_operator_reads_the_cell_with_the_count_the_cap_checks_and_its_cap()
    {
        await using (var factory = await StartAsync())
        {
            using var client = CreateClient(factory, _console);
            var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

            using var read = await GetAsync(client, "/api/cell", token);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var cell = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("vn-1", cell.GetProperty("id").GetString());
            Assert.Equal("vn", cell.GetProperty("region").GetString());
            Assert.Equal(4, cell.GetProperty("tenants").GetInt32());
            Assert.Equal(JsonValueKind.Null, cell.GetProperty("cap").ValueKind);
            Assert.Equal(JsonValueKind.Null, cell.GetProperty("restoreCheck").ValueKind);

            await ExecuteAsync(
                "INSERT INTO platform.restore_checks (id, ran_at, tenant_id, passed) VALUES " +
                "(gen_random_uuid(), now() - interval '8 days', 'a', true), (gen_random_uuid(), now() - interval '1 day', 'b', false)");
            using var afterCheck = await GetAsync(client, "/api/cell", token);

            Assert.Equal(HttpStatusCode.OK, afterCheck.StatusCode);
            var restoreCheck = (await afterCheck.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("restoreCheck");
            Assert.Equal("b", restoreCheck.GetProperty("tenantId").GetString());
            Assert.False(restoreCheck.GetProperty("passed").GetBoolean());
            Assert.InRange(DateTimeOffset.UtcNow - restoreCheck.GetProperty("ranAt").GetDateTimeOffset(), TimeSpan.FromHours(23), TimeSpan.FromHours(25));
        }

        await using (var capped = await StartAsync(tenantCap: 10))
        {
            using var client = CreateClient(capped, _console);
            var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

            using var read = await GetAsync(client, "/api/cell", token);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var cell = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal(4, cell.GetProperty("tenants").GetInt32());
            Assert.Equal(10, cell.GetProperty("cap").GetInt32());

            using var queued = await PostJobAsync(client, "/api/cell/tenants", token, "count-d", CreateBody("d"));
            using var afterQueue = await GetAsync(client, "/api/cell", token);

            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            Assert.Equal(5, (await afterQueue.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("tenants").GetInt32());
        }
    }

    [Fact]
    public async Task An_operator_lists_the_jobs_newest_first_filtered_by_tenant_and_limited_without_the_actor()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var ids = new List<Guid>();
        foreach (var (path, key) in new[]
        {
            ("/api/cell/tenants/b/suspend", "list-1"),
            ("/api/cell/tenants/b/resume", "list-2"),
            ("/api/cell/tenants/c/suspend", "list-3"),
        })
        {
            using var queued = await PostJobAsync(client, path, token, key);
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            ids.Add((await queued.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        }

        await ExecuteAsync($"UPDATE platform.jobs SET created_at = now() - interval '3 hours' WHERE id = '{ids[0]:D}'");
        await ExecuteAsync($"UPDATE platform.jobs SET created_at = now() - interval '2 hours' WHERE id = '{ids[1]:D}'");
        await ExecuteAsync($"UPDATE platform.jobs SET created_at = now() - interval '1 hour' WHERE id = '{ids[2]:D}'");
        await ExecuteAsync(
            $$"""
            UPDATE platform.jobs SET state = 'failed', error = 'The tenant is still provisioning.',
                steps = '[{"name":"status","state":"failed","error":"The tenant is still provisioning."}]'::jsonb
            WHERE id = '{{ids[1]:D}}'
            """);

        using var all = await GetAsync(client, "/api/cell/jobs", token);
        using var ofB = await GetAsync(client, "/api/cell/jobs?tenant=b", token);
        using var ofNobody = await GetAsync(client, "/api/cell/jobs?tenant=nobody", token);
        using var newest = await GetAsync(client, "/api/cell/jobs?limit=1", token);

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var items = (await all.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(
            [ids[2], ids[1], ids[0]],
            items.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
        Assert.All(items.EnumerateArray(), item =>
        {
            Assert.Equal(JsonValueKind.Array, item.GetProperty("steps").ValueKind);
            Assert.True(item.TryGetProperty("error", out _));
            Assert.False(item.TryGetProperty("actor", out _));
            Assert.False(item.TryGetProperty("input", out _));
        });
        Assert.Equal("status", items[0].GetProperty("steps")[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("error").ValueKind);
        Assert.Equal("failed", items[1].GetProperty("state").GetString());
        Assert.Equal("The tenant is still provisioning.", items[1].GetProperty("error").GetString());
        Assert.Equal("failed", items[1].GetProperty("steps")[0].GetProperty("state").GetString());

        var ofBItems = (await ofB.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal(
            [ids[1], ids[0]],
            ofBItems.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
        Assert.Equal(HttpStatusCode.OK, ofNobody.StatusCode);
        Assert.Equal(0, (await ofNobody.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items").GetArrayLength());
        var newestItems = (await newest.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("items");
        Assert.Equal([ids[2]], newestItems.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());

        foreach (var limit in new[] { "0", "201", "-1", "abc", "" })
        {
            using var bad = await GetAsync(client, $"/api/cell/jobs?limit={limit}", token);

            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal("application/problem+json", bad.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task The_cell_and_job_list_routes_answer_401_without_a_token_403_without_the_role_and_404_for_a_tenant_host()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        using var tenantHostClient = CreateClient(factory, _tenantHost);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        foreach (var path in new[] { "/api/cell", "/api/cell/jobs" })
        {
            using var none = await GetAsync(client, path, token: null);
            using var noRole = await GetAsync(client, path, noRoleToken);
            using var tenantHost = await GetAsync(tenantHostClient, path, operatorToken);

            Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, tenantHost.StatusCode);
        }
    }

    [Fact]
    public async Task The_job_routes_answer_401_without_a_token_403_without_the_role_and_404_for_an_unknown_tenant_job_or_tenant_host()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        using var tenantHostClient = CreateClient(factory, _tenantHost);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        foreach (var kind in new[] { "suspend", "resume" })
        {
            using var none = await PostJobAsync(client, $"/api/cell/tenants/b/{kind}", token: null, "k");
            using var noRole = await PostJobAsync(client, $"/api/cell/tenants/b/{kind}", noRoleToken, "k");
            using var unknown = await PostJobAsync(client, $"/api/cell/tenants/nobody/{kind}", operatorToken, "k");
            using var preview = await PostJobAsync(client, $"/api/cell/tenants/a-preview/{kind}", operatorToken, "k");
            using var tenantHost = await PostJobAsync(tenantHostClient, $"/api/cell/tenants/b/{kind}", operatorToken, "k");

            Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, tenantHost.StatusCode);
        }

        var jobId = Guid.NewGuid();
        using var noJob = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", operatorToken);
        using var noToken = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", token: null);
        using var noRoleJob = await GetAsync(client, $"/api/cell/jobs/{jobId:D}", noRoleToken);
        using var tenantHostJob = await GetAsync(tenantHostClient, $"/api/cell/jobs/{jobId:D}", operatorToken);

        Assert.Equal(HttpStatusCode.NotFound, noJob.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRoleJob.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tenantHostJob.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task A_create_with_the_id_of_a_deleted_tenant_answers_409_and_queues_no_job()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', now() - interval '200 days', 'test', '{}')");

        using var response = await PostJobAsync(client, "/api/cell/tenants", token, "create-gone", CreateBody("gone"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task A_host_of_a_tenant_deleted_10_days_ago_answers_409_without_naming_the_tenant()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        await ExecuteAsync("INSERT INTO platform.deleted_tenants (id, deleted_at, actor, hosts) VALUES ('gone', now() - interval '10 days', 'test', '{gone.vn.example.test}')");

        using var response = await SendAsync(client, HttpMethod.Post, "/api/cell/tenants/b/hosts", token, new { host = "GONE.vn.example.test" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("gone", body, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync("platform.tenant_host_requests"));
    }

    [Fact]
    public async Task An_operator_queues_a_create_job_with_five_pending_steps_and_the_job_read_hides_its_input()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var create = await PostJobAsync(client, "/api/cell/tenants", token, "create-d", CreateBody("d"));

        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        var queued = await create.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("create", queued.GetProperty("kind").GetString());
        Assert.Equal("d", queued.GetProperty("tenantId").GetString());
        Assert.Equal("queued", queued.GetProperty("state").GetString());
        var id = queued.GetProperty("id").GetGuid();
        Assert.Equal($"/api/cell/jobs/{id:D}", create.Headers.Location?.OriginalString);

        using var read = await GetAsync(client, $"/api/cell/jobs/{id:D}", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var job = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(
            ["tenant", "application", "activate", "invite", "resume"],
            job.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("name").GetString()));
        Assert.All(job.GetProperty("steps").EnumerateArray(), step => Assert.Equal("pending", step.GetProperty("state").GetString()));
        Assert.False(job.TryGetProperty("input", out _));
        Assert.Equal(
            """{"application":"approvals","administrator":"admin@example.test"}""",
            await ScalarAsync("SELECT input::text FROM platform.jobs") is string input ? Compact(input) : null);
    }

    [Fact]
    public async Task A_create_without_an_application_answers_202_and_stores_no_application_in_the_job_input()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var create = await PostJobAsync(client, "/api/cell/tenants", token, "create-blank", CreateBody("d", application: null));

        Assert.Equal(HttpStatusCode.Accepted, create.StatusCode);
        Assert.Equal("""{"administrator":"admin@example.test"}""", await ScalarAsync("SELECT input::text FROM platform.jobs") is string input ? Compact(input) : null);
    }

    [Fact]
    public async Task A_create_from_the_built_in_approvals_application_answers_202_when_it_is_installed_and_400_when_it_is_not()
    {
        using var empty = new TemporaryFolder();
        await using (var without = await StartAsync(templates: empty.Path))
        {
            using var client = CreateClient(without, _console);
            var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

            using var response = await PostJobAsync(client, "/api/cell/tenants", token, "approvals-missing", CreateBody("d", application: "approvals"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(0, await CountAsync("platform.jobs"));
        }

        await using var with = await StartAsync();
        using var installedClient = CreateClient(with, _console);
        var installedToken = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var created = await PostJobAsync(installedClient, "/api/cell/tenants", installedToken, "approvals-installed", CreateBody("d", application: "approvals"));

        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        Assert.Equal("create", (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("kind").GetString());
        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task An_id_of_an_existing_tenant_or_an_unfinished_create_job_answers_409()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var existing = await PostJobAsync(client, "/api/cell/tenants", token, "k-b", CreateBody("b"));
        using var preview = await PostJobAsync(client, "/api/cell/tenants", token, "k-preview", CreateBody("a-preview"));
        using var first = await PostJobAsync(client, "/api/cell/tenants", token, "k-d", CreateBody("d"));
        using var queued = await PostJobAsync(client, "/api/cell/tenants", token, "k-d-again", CreateBody("d"));

        Assert.Equal(HttpStatusCode.Conflict, existing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, queued.StatusCode);
        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task A_cell_at_its_tenant_cap_answers_409_this_cell_is_full_and_counts_unfinished_create_jobs()
    {
        // The fixture holds the directory rows a, b, c and a-preview, so a cap of 4 is reached.
        await using (var full = await StartAsync(tenantCap: 4))
        {
            using var client = CreateClient(full, _console);
            var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

            using var response = await PostJobAsync(client, "/api/cell/tenants", token, "full-d", CreateBody("d"));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            Assert.Equal("This cell is full.", problem.GetProperty("title").GetString());
            Assert.Equal(0, await CountAsync("platform.jobs"));
        }

        await using var room = await StartAsync(tenantCap: 5);
        using var roomClient = CreateClient(room, _console);
        var roomToken = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var last = await PostJobAsync(roomClient, "/api/cell/tenants", roomToken, "room-d", CreateBody("d"));
        using var over = await PostJobAsync(roomClient, "/api/cell/tenants", roomToken, "room-e", CreateBody("e"));

        Assert.Equal(HttpStatusCode.Accepted, last.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Equal("This cell is full.", (await over.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("title").GetString());
        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task Creating_without_the_role_answers_403_and_a_bad_request_answers_400_with_no_job_queued()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var operatorToken = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var noRole = await PostJobAsync(client, "/api/cell/tenants", noRoleToken, "k-1", CreateBody("d"));
        using var noKey = await PostJobAsync(client, "/api/cell/tenants", operatorToken, key: null, CreateBody("d"));
        using var unknownTemplate = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-2", CreateBody("d", application: "nothing"));
        using var upperCase = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-3", CreateBody("Upper"));
        using var reserved = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-4", CreateBody("master"));
        using var tooLong = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-5", CreateBody(new string('a', 51)));
        using var badEmail = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-6", CreateBody("d", administrator: "not-an-email"));
        using var notJson = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-7", "{");
        using var noBody = await PostJobAsync(client, "/api/cell/tenants", operatorToken, "k-8");

        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownTemplate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, upperCase.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noBody.StatusCode);
        Assert.Equal(0, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task A_create_with_quotas_stores_them_in_the_job_input_and_an_unknown_key_a_negative_value_or_other_quotas_for_the_same_key_answer_422()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var valid = await PostJobAsync(client, "/api/cell/tenants", token, "quotas-1", CreateBody("d", quotas: new { users = 5 }));
        using var unknown = await PostJobAsync(client, "/api/cell/tenants", token, "quotas-2", CreateBody("e", quotas: new { seats = 5 }));
        using var negative = await PostJobAsync(client, "/api/cell/tenants", token, "quotas-3", CreateBody("e", quotas: new { users = -1 }));
        using var wrongType = await PostJobAsync(client, "/api/cell/tenants", token, "quotas-4", CreateBody("e", quotas: new { users = "many" }));
        using var other = await PostJobAsync(client, "/api/cell/tenants", token, "quotas-1", CreateBody("d", quotas: new { users = 6 }));

        Assert.Equal(HttpStatusCode.Accepted, valid.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, negative.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        var input = JsonSerializer.Deserialize<JsonElement>((string)(await ScalarAsync("SELECT input::text FROM platform.jobs"))!);
        Assert.Equal("""{"users":5}""", Compact(input.GetProperty("quotas").GetRawText()));
        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task The_same_key_and_body_sent_after_the_tenant_exists_answer_the_first_job_and_another_body_answers_422()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var first = await PostJobAsync(client, "/api/cell/tenants", token, "create-once", CreateBody("d"));
        await ExecuteAsync(
            """
            INSERT INTO platform.tenants (
                id, status, database_host, database_port, database_name, runtime_user, schema_user,
                runtime_password, schema_password, oidc_authority, oidc_client_id, oidc_client_secret)
            VALUES ('d', 'provisioning', 'localhost', 5432, 'db', 'r', 's', '\x00', '\x00', 'http://idp.test', 'axis', '\x00')
            """);
        using var again = await PostJobAsync(client, "/api/cell/tenants", token, "create-once", CreateBody("d"));
        using var other = await PostJobAsync(client, "/api/cell/tenants", token, "create-once", CreateBody("e"));
        using var withoutKey = await PostJobAsync(client, "/api/cell/tenants", token, "create-twice", CreateBody("d"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid(),
            (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withoutKey.StatusCode);
        Assert.Equal(1, await CountAsync("platform.jobs"));
    }

    [Fact]
    public async Task Parallel_creates_of_one_id_with_different_keys_queue_one_job()
    {
        await using var factory = await StartAsync();
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(index => PostJobAsync(client, "/api/cell/tenants", token, $"race-{index}", CreateBody("d"))));
        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
            Assert.Equal(5, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await CountAsync("platform.jobs"));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_tenant_realm_token_and_a_token_without_the_role_cannot_download_an_export_and_no_record_is_written()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, _) = await SeedExportAsync("b");
        using var client = CreateClient(factory, _console);
        var tenantRealmToken = await TokenAsync("axis", KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret);
        var noRoleToken = await TokenAsync(_realm, NoRoleClientId, ClientSecret);

        using var none = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token: null);
        using var tenantRealm = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", tenantRealmToken);
        using var noRole = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", noRoleToken);
        using var noRoleJson = await GetAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", noRoleToken);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, tenantRealm.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noRoleJson.StatusCode);
        Assert.Equal(0, await DownloadRecordsAsync());
    }

    [Fact]
    public async Task An_operator_download_returns_the_stored_zip_and_writes_one_record_naming_the_operator_and_the_export()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, bytes) = await SeedExportAsync("b");
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var download = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync(CancellationToken));
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id, actor, array_to_string(changed, ',') FROM platform.audit_records WHERE command = 'tenant export download'", connection);
        await using var records = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await records.ReadAsync(CancellationToken));
        Assert.Equal(("b", new JsonWebToken(token).Subject, $"export:{exportId:D}"), (records.GetString(0), records.GetString(1), records.GetString(2)));
        Assert.False(await records.ReadAsync(CancellationToken));
    }

    [Fact]
    public async Task The_export_read_answers_its_fields_and_404_for_an_export_of_another_tenant_an_unknown_export_and_a_preview_tenant()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, _) = await SeedExportAsync("b");
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var read = await GetAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        using var otherTenant = await GetAsync(client, $"/api/cell/tenants/c/exports/{exportId:D}", token);
        using var otherTenantDownload = await DownloadAsync(client, $"/api/cell/tenants/c/exports/{exportId:D}", token);
        using var unknownExport = await GetAsync(client, $"/api/cell/tenants/b/exports/{Guid.NewGuid():D}", token);
        using var unknownTenant = await GetAsync(client, $"/api/cell/tenants/nobody/exports/{exportId:D}", token);
        using var preview = await GetAsync(client, $"/api/cell/tenants/a-preview/exports/{exportId:D}", token);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var export = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal(exportId, export.GetProperty("id").GetGuid());
        Assert.Equal("b", export.GetProperty("tenantId").GetString());
        Assert.Equal("succeeded", export.GetProperty("state").GetString());
        var createdAt = export.GetProperty("createdAt").GetDateTimeOffset();
        var expiresAt = export.GetProperty("expiresAt").GetDateTimeOffset();
        Assert.True(createdAt > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.InRange(expiresAt, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(5));
        Assert.Equal(HttpStatusCode.NotFound, otherTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherTenantDownload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownExport.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownTenant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(0, await DownloadRecordsAsync());
    }

    [Fact]
    public async Task An_export_that_has_not_succeeded_has_no_expiry_and_a_download_answers_409_and_writes_no_record()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, _) = await SeedExportAsync("b", state: "running");
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var read = await GetAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        using var download = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var export = await read.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.Equal("running", export.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, export.GetProperty("expiresAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, download.StatusCode);
        Assert.Equal(0, await DownloadRecordsAsync());
    }

    [Fact]
    public async Task A_server_without_the_export_storage_settings_still_reads_an_export_and_a_download_answers_503()
    {
        await using var factory = await StartAsync();
        var (exportId, _) = await SeedExportAsync("b");
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var read = await GetAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        using var download = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, download.StatusCode);
        Assert.Equal(0, await DownloadRecordsAsync());
    }

    [Fact]
    public async Task The_sweep_deletes_a_zip_stored_more_than_7_days_ago_and_the_download_then_answers_410()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, _) = await SeedExportAsync("b");
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);
        var sweep = new TenantExportSweep(_exportStorage!);
        var output = new StringWriter();

        await sweep.RunRoundAsync(DateTimeOffset.UtcNow, output, CancellationToken);
        Assert.Equal([TenantExports.Key("b", exportId)], await KeysAsync());
        using var kept = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);

        await sweep.RunRoundAsync(DateTimeOffset.UtcNow.AddDays(8), output, CancellationToken);

        Assert.Empty(await KeysAsync());
        Assert.Equal($"b export deleted: {TenantExports.Key("b", exportId)}{Environment.NewLine}", output.ToString());
        using var gone = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        Assert.Equal(HttpStatusCode.Gone, gone.StatusCode);
        Assert.Equal(1, await DownloadRecordsAsync());
    }

    [Fact]
    public async Task An_export_whose_job_finished_more_than_7_days_ago_answers_410_on_the_read_and_the_download()
    {
        await using var factory = await StartAsync(exportBucket: true);
        var (exportId, _) = await SeedExportAsync("b", finishedAgo: TimeSpan.FromDays(8));
        using var client = CreateClient(factory, _console);
        var token = await TokenAsync(_realm, CallerClientId, ClientSecret);

        using var read = await GetAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);
        using var download = await DownloadAsync(client, $"/api/cell/tenants/b/exports/{exportId:D}", token);

        Assert.Equal(HttpStatusCode.Gone, read.StatusCode);
        Assert.Equal(HttpStatusCode.Gone, download.StatusCode);
        Assert.Equal(0, await DownloadRecordsAsync());
    }

    // An export job of the tenant, and its ZIP in the bucket of the export storage when the server has one.
    private async Task<(Guid Id, byte[] Bytes)> SeedExportAsync(string tenant, string state = "succeeded", TimeSpan? finishedAgo = null)
    {
        var id = Guid.CreateVersion7();
        var bytes = RandomNumberGenerator.GetBytes(256);
        var finished = DateTimeOffset.UtcNow - (finishedAgo ?? TimeSpan.Zero);
        await ExecuteAsync(
            $"""
            INSERT INTO platform.jobs (id, kind, tenant_id, state, steps, actor, created_at, finished_at)
            VALUES ('{id:D}', 'export', '{tenant}', '{state}', '[]', 'operator-1', '{finished - TimeSpan.FromMinutes(1):O}',
                    {(state == "succeeded" ? $"'{finished:O}'" : "NULL")})
            """);
        if (_exportStorage is { } storage)
        {
            using var s3 = storage.NewClient();
            await MinioFixture.UploadObjectAsync(s3, storage.Bucket, TenantExports.Key(tenant, id), bytes, CancellationToken);
        }

        return (id, bytes);
    }

    private async Task<List<string>> KeysAsync()
    {
        using var s3 = _exportStorage!.NewClient();
        var response = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _exportStorage.Bucket }, CancellationToken);
        return [.. (response.S3Objects ?? []).Select(item => item.Key).Order(StringComparer.Ordinal)];
    }

    private async Task<long> DownloadRecordsAsync() =>
        (long)(await ScalarAsync($"SELECT count(*) FROM platform.audit_records WHERE command = '{TenantExports.DownloadCommand}'"))!;

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient client, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    // A job row as the publisher left it, for the run-again routes. Its steps are a JSON array, and the newest job of a tenant and kind is the one made last.
    private async Task<Guid> SeedJobAsync(string kind, string tenant, string state, string steps, string? input = null, TimeSpan? createdAgo = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO platform.jobs (id, kind, tenant_id, state, steps, input, error, actor, created_at, claims)
            VALUES (@id, @kind, @tenant, @state, @steps::jsonb, @input::jsonb, @error, 'operator-1', @createdAt, 3)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("steps", steps);
        command.Parameters.AddWithValue("input", (object?)input ?? DBNull.Value);
        command.Parameters.AddWithValue("error", state == "failed" ? "A step failed." : DBNull.Value);
        command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow - (createdAgo ?? TimeSpan.Zero) - TimeSpan.FromMinutes(1));
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static string CreateSteps(params string[] states) =>
        JsonSerializer.Serialize(_createStepNames.Zip(
            states, (name, state) => new { name, state, error = state == "failed" ? "The step failed." : null }));

    private async Task<bool> RunJobAsync() =>
        await new ProvisioningJobs(_platform, ProvisioningJobs.Builtin(_platform), TimeSpan.FromMinutes(5))
            .RunNextAsync(TextWriter.Null, CancellationToken);

    // A bearer call needs no CSRF header, so a request with a token never sets it.
    private static async Task<HttpResponseMessage> PostJobAsync(HttpClient client, string path, string? token, string? key, string? body = null, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static void AssertQuota(JsonElement quotas, string key, long? value, bool overridden)
    {
        var quota = quotas.GetProperty(key);
        Assert.Equal(value, quota.GetProperty("value").ValueKind == JsonValueKind.Null ? null : quota.GetProperty("value").GetInt64());
        Assert.Equal(overridden, quota.GetProperty("overridden").GetBoolean());
    }

    // A null application is left out of the body, as a request for a blank tenant leaves it out.
    private static string CreateBody(string id, string? application = "approvals", string administrator = "admin@example.test", object? quotas = null)
    {
        var body = new Dictionary<string, object?> { ["id"] = id };
        if (application is not null)
        {
            body["application"] = application;
        }

        body["administrator"] = administrator;
        if (quotas is not null)
        {
            body["quotas"] = quotas;
        }

        return JsonSerializer.Serialize(body);
    }

    private static string Compact(string json) => JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(json));

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<List<(string Command, string Tenant, string Changed)>> AuditRecordsAsync()
    {
        await using var connection = new NpgsqlConnection(_platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, array_to_string(changed, ',') FROM platform.audit_records ORDER BY occurred_at, id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var records = new List<(string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return records;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object body)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            // A write without a bearer token is a session write, which needs the CSRF header, so the answer comes from authentication.
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    // Every leaf of the response is on the allowed paths, and no string leaf equals or contains a forbidden value. A path names
    // the properties down to the leaf, an array as [] and the key of a quota as *. A null failedWork is a leaf of its own.
    private static void AssertFields(JsonElement response, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> forbidden)
    {
        var leaves = new List<(string Path, JsonElement Value)>();
        Walk(response, "", leaves);
        Assert.NotEmpty(leaves);
        foreach (var (path, value) in leaves)
        {
            Assert.Contains(path, allowed);
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()!;
                Assert.DoesNotContain(forbidden, secret => text.Contains(secret, StringComparison.Ordinal));
            }
        }

        static void Walk(JsonElement element, string path, List<(string, JsonElement)> leaves)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var name = path == "quotas" ? "*" : property.Name;
                        Walk(property.Value, path.Length == 0 ? name : $"{path}.{name}", leaves);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item, $"{path}[]", leaves);
                    }

                    break;
                default:
                    leaves.Add((path, element));
                    break;
            }
        }
    }

    private static (long? Limit, long? Used) Use(JsonElement tenant, string quota)
    {
        var use = tenant.GetProperty("usage").GetProperty(quota);
        return (Count(use.GetProperty("limit")), Count(use.GetProperty("used")));

        static long? Count(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
    }

    private static (long Instances, long UnknownCalls, long DeadLetters)? FailedWork(JsonElement tenant)
    {
        var counts = tenant.GetProperty("failedWork");
        return counts.ValueKind == JsonValueKind.Null
            ? null
            : (counts.GetProperty("instances").GetInt64(), counts.GetProperty("unknownCalls").GetInt64(), counts.GetProperty("deadLetters").GetInt64());
    }

    private async Task<string> CreateRealmUserAsync(
        string adminToken, string realm, string email, string firstName, string lastName, bool enabled, string locale)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseAddress, $"admin/realms/{realm}/users"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(new
        {
            username = email,
            email,
            firstName,
            lastName,
            enabled,
            attributes = new Dictionary<string, string[]> { ["locale"] = [locale] },
        });
        using var response = await _http.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        return location[(location.LastIndexOf('/') + 1)..];
    }

    private static string[] Hosts(JsonElement tenant) =>
        [.. tenant.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()!)];

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, Uri address) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = address, AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<string> TokenAsync(string realm, string clientId, string secret)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        });
        using var response = await _http.PostAsync(
            new Uri(_baseAddress, $"realms/{realm}/protocol/openid-connect/token"), content, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<WebApplicationFactory<Program>> StartAsync(int? tenantCap = null, string? templates = null, bool exportBucket = false, LogCollector? logs = null)
    {
        var authority = await keycloak.StartAsync();
        var platform = await CellTestPlatform.CreateAsync(database, authority, CancellationToken);
        _platform = platform;
        if (exportBucket)
        {
            // A bucket of its own, so no test sees another's objects.
            var bucket = $"exports-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6))}";
            _exportStorage = new CellBackupStorage(minio.ServiceUrl, "us-east-1", bucket, minio.AccessKey, minio.SecretKey);
            using var s3 = _exportStorage.NewClient();
            await MinioFixture.CreateBucketAsync(s3, bucket, CancellationToken);
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("Cell:Id", "vn-1");
            builder.UseSetting("Cell:Region", "vn");
            builder.UseSetting("Cell:TenantDomain", "vn.example.test");
            builder.UseSetting("Cell:ConsoleHost", _console.Host);
            builder.UseSetting("Cell:OperatorRealm", _realm);
            builder.UseSetting("Cell:OperatorRole", OperatorRole);
            builder.UseSetting("IdentityProvider:Url", _baseAddress.ToString());
            builder.UseSetting("Templates:Path", templates ?? BuiltInApplications.Path);
            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }

            if (_exportStorage is { } storage)
            {
                builder.UseSetting("Cell:ExportStorage:ServiceUrl", storage.ServiceUrl);
                builder.UseSetting("Cell:ExportStorage:Region", storage.Region);
                builder.UseSetting("Cell:ExportStorage:Bucket", storage.Bucket);
                builder.UseSetting("Cell:ExportStorage:AccessKeyId", storage.AccessKeyId);
                builder.UseSetting("Cell:ExportStorage:SecretAccessKey", storage.SecretAccessKey);
            }

            if (tenantCap is { } cap)
            {
                builder.UseSetting("Cell:TenantCap", cap.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        });
    }
}
