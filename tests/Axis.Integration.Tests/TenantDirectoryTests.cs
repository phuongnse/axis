using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class TenantDirectoryTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string TestSecretKey = TestPlatform.SecretKey;
    private const string Departments = "/api/apps/RecordsApp/entities/Department/records";

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_directory_tenant_serves_only_its_own_records_and_an_unknown_host_is_404()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");

        await CreateDepartmentAsync(client, "a.example.test", "Sales-a");
        await CreateDepartmentAsync(client, "b.example.test", "Sales-b");

        var listA = await ListAsync(client, "a.example.test");
        var listB = await ListAsync(client, "b.example.test");
        Assert.Contains("Sales-a", listA, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales-b", listA, StringComparison.Ordinal);
        Assert.Contains("Sales-b", listB, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales-a", listB, StringComparison.Ordinal);
        using var unknown = await client.SendAsync(Request(Departments, "c.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task No_log_and_no_500_problem_holds_a_database_password_or_a_connection_string()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(
            a with { Host = "a.example.test" },
            a with { Id = "c", Host = "c.example.test", DatabaseHost = "127.0.0.1", DatabasePort = 1 });
        var logs = new LogCollector();
        await using var factory = CreateFactory(platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");

        await CreateDepartmentAsync(client, "a.example.test", "Sales-a");
        Assert.Contains("Sales-a", await ListAsync(client, "a.example.test"), StringComparison.Ordinal);
        using var broken = await client.SendAsync(Request(Departments, "c.example.test"), CancellationToken);
        var body = await broken.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
        Assert.NotEmpty(logs.Entries);
        Assert.All(logs.Entries.Append(body), text =>
        {
            Assert.DoesNotContain(role.Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Host=", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task A_Tenants_section_is_ignored_with_a_warning_and_its_host_is_404()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        var logs = new LogCollector();
        await using var factory = CreateFactory(
            platform,
            logs,
            ("Tenants:x:Hosts:0", "x.example.test"),
            ("Tenants:x:ConnectionString", a.RuntimeConnectionString()));
        using var client = factory.CreateClient().SignedInAs("requester");

        using var ignored = await client.SendAsync(Request(Departments, "x.example.test"), CancellationToken);
        using var served = await client.SendAsync(Request(Departments, "a.example.test"), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, ignored.StatusCode);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains(
            logs.Entries,
            entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("Tenants section is ignored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_start_stops_naming_ConnectionStrings_Platform_when_the_platform_database_cannot_be_read()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        var wrongPassword = $"wrong{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";
        var unreadable = new NpgsqlConnectionStringBuilder(PostgreSqlServer.RuntimeConnectionString(platform)) { Password = wrongPassword }.ConnectionString;
        await using var factory = CreateFactory(platform, new LogCollector(), ("ConnectionStrings:Platform", unreadable));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Platform", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(wrongPassword, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_start_stops_naming_the_tenant_and_the_binding_but_not_the_value_when_a_url_binding_is_not_a_url()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(
            [new TenantBindingRow { TenantId = "a", Name = "PurchasingUrl", Kind = DeploymentBindingKinds.Url, Url = "not-a-url/secretmarker" }],
            a with { Host = "a.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Tenant 'a' binding 'PurchasingUrl' is not an absolute http or https URL.", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secretmarker", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_added_to_the_directory_is_served_after_one_refresh_without_a_restart()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        using var before = await client.SendAsync(Request(Departments, "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        await AddTenantsAsync(platform, b with { Host = "b.example.test" });
        await RefreshAsync(factory);

        await CreateDepartmentAsync(client, "b.example.test", "Sales-b");
        Assert.Contains("Sales-b", await ListAsync(client, "b.example.test"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_suspended_tenant_answers_503_problem_details_that_name_no_tenant_and_no_host()
    {
        var role = NewRole();
        var quiz = await PrepareTenantAsync("quiz", role);
        var platform = await PreparePlatformAsync(quiz with { Host = "quiz.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        await ListAsync(client, "quiz.example.test");

        await UpdateTenantAsync(platform, "quiz", row => row.Status = "suspended");
        await RefreshAsync(factory);

        using var response = await client.SendAsync(Request(Departments, "quiz.example.test"), CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("This tenant is not available.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("quiz", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", body, StringComparison.OrdinalIgnoreCase);

        await UpdateTenantAsync(platform, "quiz", row => row.Status = "active");
        await RefreshAsync(factory);
        await ListAsync(client, "quiz.example.test");
    }

    [Fact]
    public async Task A_read_only_tenant_still_serves_its_routes_and_the_console_reports_its_status()
    {
        var role = NewRole();
        var quiz = await PrepareTenantAsync("quiz", role);
        var platform = await PreparePlatformAsync(quiz with { Host = "quiz.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector(), ("Console:OperatorRole", "console-operator"));
        using var client = factory.CreateClient().SignedInAs("requester");
        await ListAsync(client, "quiz.example.test");

        await UpdateTenantAsync(platform, "quiz", row => row.Status = "readOnly");
        await RefreshAsync(factory);

        await ListAsync(client, "quiz.example.test");
        using var refusedWrite = await client.SendAsync(
            Request(HttpMethod.Post, Departments, "quiz.example.test", """{ "values": { "name": "Refused" } }"""),
            CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refusedWrite.StatusCode);
        using var refusal = JsonDocument.Parse(await refusedWrite.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("tenant-read-only", refusal.RootElement.GetProperty("reason").GetString());
        using var console = factory.CreateClient().SignedInAs("operator-1");
        using var response = await console.SendAsync(Request("/api/console/tenant", "quiz.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("readOnly", body.RootElement.GetProperty("status").GetString());

        await UpdateTenantAsync(platform, "quiz", row => row.Status = "active");
        await RefreshAsync(factory);
        using var active = await console.SendAsync(Request("/api/console/tenant", "quiz.example.test"), CancellationToken);
        using var activeBody = JsonDocument.Parse(await active.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("active", activeBody.RootElement.GetProperty("status").GetString());
        await CreateDepartmentAsync(client, "quiz.example.test", "Accepted");
    }

    [Fact]
    public async Task Me_answers_the_status_of_the_tenant_and_no_billing_address_outside_a_cell()
    {
        var role = NewRole();
        var quiz = await PrepareTenantAsync("quiz", role);
        var platform = await PreparePlatformAsync(quiz with { Host = "quiz.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector(), ("Users:AdministratorRole", "author"));
        using var client = factory.CreateClient().SignedInAs("author");

        var active = await ReadMeAsync(client, "quiz.example.test");
        Assert.Equal("active", active.GetProperty("tenantStatus").GetString());
        Assert.Equal(JsonValueKind.Null, active.GetProperty("billingUrl").ValueKind);

        await UpdateTenantAsync(platform, "quiz", row => row.Status = "readOnly");
        await RefreshAsync(factory);

        var readOnly = await ReadMeAsync(client, "quiz.example.test");
        Assert.Equal("readOnly", readOnly.GetProperty("tenantStatus").GetString());
        Assert.Equal(JsonValueKind.Null, readOnly.GetProperty("billingUrl").ValueKind);
    }

    [Fact]
    public async Task Me_answers_the_billing_address_of_the_tenant_in_a_cell_to_an_administrator_only()
    {
        var role = NewRole();
        var quiz = await PrepareTenantAsync("quiz", role);
        var platform = await PreparePlatformAsync(quiz with { Host = "quiz.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector(), [.. _cellSettings, ("Users:AdministratorRole", "author")]);
        using var administrator = factory.CreateClient().SignedInAs("author");
        using var member = factory.CreateClient().SignedInAs("requester");

        var administratorMe = await ReadMeAsync(administrator, "quiz.example.test");
        Assert.Equal("https://account.vn.example.test/tenants/quiz", administratorMe.GetProperty("billingUrl").GetString());

        var memberMe = await ReadMeAsync(member, "quiz.example.test");
        Assert.Equal(JsonValueKind.Null, memberMe.GetProperty("billingUrl").ValueKind);
    }

    private static async Task<JsonElement> ReadMeAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request("/api/me", host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.Clone();
    }

    [Fact]
    public async Task A_host_removed_from_a_tenant_is_404_after_a_refresh_and_its_other_host_still_serves()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await using (var context = PlatformContext(platform))
        {
            context.TenantHosts.Add(new TenantHostRow { TenantId = "a", Host = "a2.example.test" });
            await context.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        await ListAsync(client, "a2.example.test");

        await using (var context = PlatformContext(platform))
        {
            await context.TenantHosts.Where(host => host.Host == "a2.example.test").ExecuteDeleteAsync(CancellationToken);
        }

        await RefreshAsync(factory);

        using var removed = await client.SendAsync(Request(Departments, "a2.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        await ListAsync(client, "a.example.test");
    }

    [Fact]
    public async Task A_new_tenant_on_a_runtime_user_that_owns_the_axis_schema_answers_503_until_it_passes_the_role_check()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        var unsafeRole = new Role(PostgreSqlServer.SchemaRole, PostgreSqlServer.RolePassword);

        await AddTenantsAsync(platform, b with { Host = "b.example.test", Role = unsafeRole });
        await RefreshAsync(factory);

        using var refused = await client.SendAsync(Request(Departments, "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        await ListAsync(client, "a.example.test");

        await SetRuntimeRoleAsync(platform, "b", role);
        await RefreshAsync(factory);
        await ListAsync(client, "b.example.test");
    }

    [Fact]
    public async Task A_served_tenant_switched_to_a_runtime_user_that_owns_the_axis_schema_answers_503_until_it_passes_again()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        await ListAsync(client, "a.example.test");

        await SetRuntimeRoleAsync(platform, "a", new Role(PostgreSqlServer.SchemaRole, PostgreSqlServer.RolePassword));
        await RefreshAsync(factory);

        using var refused = await client.SendAsync(Request(Departments, "a.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        await SetRuntimeRoleAsync(platform, "a", role);
        await RefreshAsync(factory);
        await ListAsync(client, "a.example.test");
    }

    [Fact]
    public async Task A_tenant_whose_database_name_changes_gets_a_new_pool_and_lists_the_records_of_the_new_database()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = factory.CreateClient().SignedInAs("requester");
        await CreateDepartmentAsync(client, "a.example.test", "Sales-a");
        await CreateDepartmentAsync(client, "b.example.test", "Sales-b");

        await UpdateTenantAsync(platform, "a", row => row.DatabaseName = b.DatabaseName);
        await RefreshAsync(factory);

        var list = await ListAsync(client, "a.example.test");
        Assert.Contains("Sales-b", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales-a", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_server_applies_no_tenant_migration_under_directory()
    {
        var role = NewRole();
        var schemaConnection = await database.CreateDatabaseAsync("directory_empty", CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(schemaConnection, $"CREATE ROLE {role.Name} LOGIN PASSWORD '{role.Password}'", CancellationToken);
        var builder = new NpgsqlConnectionStringBuilder(schemaConnection);
        var empty = new DirectoryTenant("empty", "empty.example.test", builder.Host!, builder.Port, builder.Database!, role);
        var platform = await PreparePlatformAsync(empty);
        await using var factory = CreateFactory(platform, new LogCollector());

        using var client = factory.CreateClient();

        Assert.Equal(
            0L,
            await PostgreSqlServer.AdminScalarAsync(
                schemaConnection,
                "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')",
                CancellationToken));
    }

    [Fact]
    public async Task A_tenant_on_another_schema_version_answers_503_and_is_served_once_it_is_on_the_servers_version()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        await UpdateTenantAsync(platform, "b", row => row.SchemaVersion = "20000101000000_Old");
        var logs = new LogCollector();
        await using var factory = CreateFactory(platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");

        await ListAsync(client, "a.example.test");
        using var held = await client.SendAsync(Request(Departments, "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, held.StatusCode);
        Assert.Contains(
            logs.Entries,
            entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("Tenant b is not served: its schema version", StringComparison.Ordinal));

        await RefreshAsync(factory);
        await RefreshAsync(factory);
        Assert.Single(logs.Entries, entry => entry.Contains("Tenant b is not served", StringComparison.Ordinal));

        await UpdateTenantAsync(platform, "b", row => row.SchemaVersion = TenantSchemaVersion.Current);
        await RefreshAsync(factory);
        await ListAsync(client, "b.example.test");

        // A served tenant that falls behind is withheld again and its pool is dropped.
        await UpdateTenantAsync(platform, "b", row => row.SchemaVersion = "29990101000000_New");
        await RefreshAsync(factory);
        using var behind = await client.SendAsync(Request(Departments, "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, behind.StatusCode);
    }

    [Fact]
    public async Task A_tenant_added_at_a_refresh_gets_its_sign_in_redirect_and_its_endpoint_caller_scheme()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var authority = await keycloak.StartAsync();
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = SignInClient(factory);
        using var before = await client.SendAsync(Request("/api/auth/sign-in", "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Null(await SchemeAsync(factory, EndpointCaller.SchemeName("b")));

        await AddTenantsAsync(platform, b with { Host = "b.example.test", OidcAuthority = authority.ToString() });
        await RefreshAsync(factory);

        Assert.StartsWith(AuthorizationEndpoint(authority), await SignInLocationAsync(client, "b.example.test"), StringComparison.Ordinal);
        Assert.NotNull(await SchemeAsync(factory, TenantSignIn.SchemeName("b")));
        Assert.NotNull(await SchemeAsync(factory, EndpointCaller.SchemeName("b")));
    }

    [Fact]
    public async Task A_tenant_suspended_at_a_refresh_answers_503_to_sign_in_and_loses_both_schemes()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var authority = await keycloak.StartAsync();
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test", OidcAuthority = authority.ToString() });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = SignInClient(factory);
        Assert.StartsWith(AuthorizationEndpoint(authority), await SignInLocationAsync(client, "a.example.test"), StringComparison.Ordinal);
        Assert.NotNull(await SchemeAsync(factory, EndpointCaller.SchemeName("a")));

        await UpdateTenantAsync(platform, "a", row => row.Status = "suspended");
        await RefreshAsync(factory);

        using var response = await client.SendAsync(Request("/api/auth/sign-in", "a.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null(await SchemeAsync(factory, TenantSignIn.SchemeName("a")));
        Assert.Null(await SchemeAsync(factory, EndpointCaller.SchemeName("a")));
    }

    [Fact]
    public async Task A_changed_oidc_authority_applies_to_sign_in_after_one_refresh()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var axis = await keycloak.StartAsync();
        var master = new Uri(axis.ToString().Replace("realms/axis", "realms/master", StringComparison.Ordinal));
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test", OidcAuthority = axis.ToString() });
        await using var factory = CreateFactory(platform, new LogCollector());
        using var client = SignInClient(factory);
        Assert.StartsWith(AuthorizationEndpoint(axis), await SignInLocationAsync(client, "a.example.test"), StringComparison.Ordinal);

        await UpdateTenantAsync(platform, "a", row => row.OidcAuthority = master.ToString());
        Assert.StartsWith(AuthorizationEndpoint(axis), await SignInLocationAsync(client, "a.example.test"), StringComparison.Ordinal);
        await RefreshAsync(factory);

        Assert.StartsWith(AuthorizationEndpoint(master), await SignInLocationAsync(client, "a.example.test"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_added_with_a_blank_oidc_authority_answers_503_and_the_other_tenants_keep_working()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        var logs = new LogCollector();
        await using var factory = CreateFactory(platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");

        await AddTenantsAsync(platform, b with { Host = "b.example.test" });
        await UpdateTenantAsync(platform, "b", row => row.OidcAuthority = "");
        await RefreshAsync(factory);

        using var refused = await client.SendAsync(Request(Departments, "b.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        await ListAsync(client, "a.example.test");
        Assert.Contains(
            logs.Entries,
            entry => entry.StartsWith("Error:", StringComparison.Ordinal) && entry.Contains("sign-in settings are complete", StringComparison.Ordinal));
        Assert.Null(await SchemeAsync(factory, TenantSignIn.SchemeName("b")));
    }

    private static HttpClient SignInClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string AuthorizationEndpoint(Uri authority) => $"{authority.ToString().TrimEnd('/')}/protocol/openid-connect/auth?";

    /// <summary>Starts sign-in on <paramref name="host"/> and returns where the redirect points.</summary>
    private static async Task<string> SignInLocationAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request("/api/auth/sign-in", host), CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location?.ToString() ?? "";
    }

    private static Task<AuthenticationScheme?> SchemeAsync(WebApplicationFactory<Program> factory, string name) =>
        factory.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(name);

    [Fact]
    public async Task A_failed_refresh_keeps_serving_the_last_good_directory_and_readiness_is_Degraded_until_a_refresh_works()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        var logs = new LogCollector();
        await using var factory = CreateFactory(platform, logs);
        using var client = factory.CreateClient().SignedInAs("requester");
        await CreateDepartmentAsync(client, "a.example.test", "Sales-a");
        var (before, beforeBody) = await ReadyAsync(client);
        Assert.Equal(HttpStatusCode.OK, before);
        Assert.Equal("""{"status":"Healthy"}""", beforeBody);

        await StopPlatformAsync(platform);
        try
        {
            await RefreshAsync(factory);
            await RefreshAsync(factory);

            Assert.Contains("Sales-a", await ListAsync(client, "a.example.test"), StringComparison.Ordinal);
            var (status, body) = await ReadyAsync(client);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("""{"status":"Degraded"}""", body);
            Assert.DoesNotContain("a.example.test", body, StringComparison.Ordinal);
            Assert.DoesNotContain(new NpgsqlConnectionStringBuilder(platform).Database!, body, StringComparison.Ordinal);
            Assert.DoesNotContain(a.DatabaseName, body, StringComparison.Ordinal);
            Assert.Equal(
                1,
                logs.Entries.Count(entry => entry.StartsWith("Error:", StringComparison.Ordinal) && entry.Contains("tenant directory", StringComparison.Ordinal)));
        }
        finally
        {
            await AllowPlatformConnectionsAsync(platform);
        }

        // A pooled connection that died with the outage must not fail the first read after it.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(platform)));
        await RefreshAsync(factory);

        var (recovered, recoveredBody) = await ReadyAsync(client);
        Assert.Equal(HttpStatusCode.OK, recovered);
        Assert.Equal("""{"status":"Healthy"}""", recoveredBody);
        Assert.Contains(
            logs.Entries,
            entry => entry.StartsWith("Information:", StringComparison.Ordinal) && entry.Contains("read again after failed reads", StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory(string platform, LogCollector logs, params (string Key, string Value)[] settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseTestPlatform(platform);

            // An hour, so the timer never races the refreshes that a test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.UseTestAuthentication(
                new TestIdentity("requester", "Requester User", "employee"),
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("eve", "Eve Exporter", "exporter"),
                new TestIdentity("operator-1", "Console Operator", "console-operator"));
        });

    [Fact]
    public async Task In_a_cell_an_author_reaches_only_the_studio_applications_of_their_tenant()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        var root = Directory.CreateTempSubdirectory("axis-studio-cell-").FullName;
        try
        {
            await using var factory = CreateFactory(
                platform, new LogCollector(), [.. _cellSettings, ("Studio:ApplicationsPath", root), ("Studio:AuthorRole", "author")]);
            using var client = factory.CreateClient().SignedInAs("author");

            foreach (var (name, host) in new[] { ("AppA", "a.example.test"), ("AppB", "b.example.test"), ("Shared", "a.example.test"), ("Shared", "b.example.test") })
            {
                using var created = await client.SendAsync(
                    Request(HttpMethod.Post, "/api/studio/apps", host, $$"""{ "name": "{{name}}" }"""), CancellationToken);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            Assert.Equal(["a", "b"], Directory.GetDirectories(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal(["app-a", "shared"], SubfolderNames(Path.Combine(root, "a")));
            Assert.Equal(["app-b", "shared"], SubfolderNames(Path.Combine(root, "b")));

            Assert.Equal(["AppA", "Shared"], await ApplicationNamesAsync(client, "a.example.test"));
            Assert.Equal(["AppB", "Shared"], await ApplicationNamesAsync(client, "b.example.test"));

            using var resources = await client.SendAsync(Request("/api/studio/apps/AppA/resources", "a.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, resources.StatusCode);
            using var list = JsonDocument.Parse(await resources.Content.ReadAsStringAsync(CancellationToken));
            var texts = list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("file").GetString() == "texts/en.json");
            var id = texts.GetProperty("id").GetString()!;
            var version = texts.GetProperty("version").GetString()!;
            using var read = await client.SendAsync(Request($"/api/studio/apps/AppA/resources/{id}", "a.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var textsFile = Path.Combine(root, "a", "app-a", "texts", "en.json");
            var before = await File.ReadAllTextAsync(textsFile, CancellationToken);
            using var patch = Request(
                HttpMethod.Patch,
                $"/api/studio/apps/AppA/resources/{id}",
                "a.example.test",
                """[{"op":"replace","path":"/texts/site.title","value":"Changed in a"}]""",
                "application/json-patch+json");
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var patched = await client.SendAsync(patch, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            var after = await File.ReadAllTextAsync(textsFile, CancellationToken);
            Assert.NotEqual(before, after);
            Assert.Contains("Changed in a", after, StringComparison.Ordinal);

            // The other tenant's application name, resource id and a path to its folder are unknown on this host.
            using var otherManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "b", "app-b", "application.json"), CancellationToken));
            var otherId = otherManifest.RootElement.GetProperty("id").GetString()!;
            using var otherTexts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "b", "app-b", "texts", "en.json"), CancellationToken));
            var otherTextsId = otherTexts.RootElement.GetProperty("id").GetString()!;
            // HttpClient removes the ".." segment of the last case, so it reaches an unknown /api route. That route answers 404
            // whether or not the SPA is built.
            var refused = new (HttpMethod Method, string Path, string? Body, string? ContentType)[]
            {
                (HttpMethod.Get, "/api/studio/apps/AppB/resources", null, null),
                (HttpMethod.Get, $"/api/studio/apps/AppA/resources/{otherId}", null, null),
                (HttpMethod.Get, $"/api/studio/apps/AppA/resources/{otherTextsId}", null, null),
                (HttpMethod.Patch, $"/api/studio/apps/AppA/resources/{otherTextsId}", """[{"op":"replace","path":"/texts/site.title","value":"x"}]""", "application/json-patch+json"),
                (HttpMethod.Patch, "/api/studio/apps/AppB/resources/" + otherTextsId, """[{"op":"replace","path":"/texts/site.title","value":"x"}]""", "application/json-patch+json"),
                (HttpMethod.Get, "/api/studio/apps/..%2Fb%2Fapp-b/resources", null, null),
                (HttpMethod.Get, "/api/studio/apps/../b/app-b/resources", null, null),
            };
            foreach (var (method, path, body, contentType) in refused)
            {
                using var request = Request(method, path, "a.example.test", body, contentType);
                request.Headers.TryAddWithoutValidation("If-Match", "\"x\"");
                using var response = await client.SendAsync(request, CancellationToken);
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{method} {path} answered {(int)response.StatusCode}.");
            }

            // The other tenant's file is untouched, and its own host still reads it.
            Assert.DoesNotContain("\"x\"", await File.ReadAllTextAsync(Path.Combine(root, "b", "app-b", "texts", "en.json"), CancellationToken), StringComparison.Ordinal);
            using var own = await client.SendAsync(Request($"/api/studio/apps/AppB/resources/{otherTextsId}", "b.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task In_a_cell_a_tenant_at_its_applications_quota_cannot_create_another_but_still_saves_and_publishes()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });

        // Tenant a already has one application with an active release, so a quota of 2 leaves room for one folder.
        await SetQuotaAsync(platform, "a", QuotaKeys.Applications, 2);
        var root = Directory.CreateTempSubdirectory("axis-studio-quota-").FullName;
        try
        {
            var logs = new LogCollector();
            await using var factory = CreateFactory(
                platform,
                logs,
                [.. _cellSettings, ("Studio:ApplicationsPath", root), ("Studio:AuthorRole", "author"), ("Studio:PublisherRole", "author")]);
            using var client = factory.CreateClient().SignedInAs("author");

            using var first = await client.SendAsync(
                Request(HttpMethod.Post, "/api/studio/apps", "a.example.test", """{ "name": "AppA" }"""), CancellationToken);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            // The tenant is at its quota, so the create is refused and writes no folder.
            using var second = await client.SendAsync(
                Request(HttpMethod.Post, "/api/studio/apps", "a.example.test", """{ "name": "AppB" }"""), CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
            using var refusal = JsonDocument.Parse(await second.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("quota-exceeded", refusal.RootElement.GetProperty("reason").GetString());
            Assert.Equal("applications", refusal.RootElement.GetProperty("quota").GetString());
            Assert.Equal(2, refusal.RootElement.GetProperty("limit").GetInt64());
            Assert.Equal(2, refusal.RootElement.GetProperty("used").GetInt64());
            Assert.Equal("This tenant has reached its applications quota.", refusal.RootElement.GetProperty("title").GetString());
            Assert.Equal(["app-a"], SubfolderNames(Path.Combine(root, "a")));
            Assert.Contains(logs.Entries, entry => entry.Contains("quota.refused: tenant a reached its applications quota.", StringComparison.Ordinal));

            // The existing application is still saved and published.
            using var resources = await client.SendAsync(Request("/api/studio/apps/AppA/resources", "a.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, resources.StatusCode);
            using var list = JsonDocument.Parse(await resources.Content.ReadAsStringAsync(CancellationToken));
            var texts = list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("file").GetString() == "texts/en.json");
            using var patch = Request(
                HttpMethod.Patch,
                $"/api/studio/apps/AppA/resources/{texts.GetProperty("id").GetString()}",
                "a.example.test",
                """[{"op":"replace","path":"/texts/site.title","value":"Changed at the quota"}]""",
                "application/json-patch+json");
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{texts.GetProperty("version").GetString()}\"");
            using var patched = await client.SendAsync(patch, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            using var published = await client.SendAsync(Request(HttpMethod.Post, "/api/studio/apps/AppA/publish", "a.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.Accepted, published.StatusCode);

            // Another tenant has the default quota and is not affected.
            using var other = await client.SendAsync(
                Request(HttpMethod.Post, "/api/studio/apps", "b.example.test", """{ "name": "AppB" }"""), CancellationToken);
            Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string[] SubfolderNames(string folder) =>
        [.. Directory.GetDirectories(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    private static async Task<string[]> ApplicationNamesAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request("/api/studio/apps", host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!)];
    }

    [Fact]
    public async Task In_a_cell_a_tenant_takes_the_default_of_each_quota_unless_it_has_a_row()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        await SetQuotaAsync(platform, "b", QuotaKeys.Users, 5);
        await SetQuotaAsync(platform, "b", QuotaKeys.Applications, null);
        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        var quotas = factory.Services.GetRequiredService<TenantQuotas>();

        Assert.Equal(QuotaDefaults.BuiltIn, quotas.For("a"));
        Assert.Equal(5, quotas.Limit("b", QuotaKeys.Users));
        Assert.Null(quotas.Limit("b", QuotaKeys.Applications));
        Assert.Equal(QuotaDefaults.BuiltIn[QuotaKeys.Storage], quotas.Limit("b", QuotaKeys.Storage));
    }

    private const string DepartmentNames = "/api/apps/RecordsApp/data-sources/DepartmentNames/rows";
    private const string ItemExport = "/api/apps/RecordsApp/entities/Item/records/export?format=csv";
    private const string DataSourceExport = "/api/apps/RecordsApp/data-sources/ItemCountsByDepartment/export?format=csv";

    [Fact]
    public async Task In_a_cell_a_read_past_concurrentQueries_waits_five_seconds_then_answers_503_and_a_freed_slot_serves_reads_without_leaving_a_lease()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await SetQuotaAsync(platform, "a", QuotaKeys.ConcurrentQueries, 1);
        await InsertLeaseAsync(platform, "a", "other", "60 seconds");
        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        using var client = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        var started = DateTime.UtcNow;
        var pending = client.SendAsync(Request(Departments, "a.example.test"), CancellationToken);

        // The sign-in check comes before the lease, so it answers at once while the slot is held and the read above still waits.
        using var unauthorized = await anonymous.SendAsync(Request(Departments, "a.example.test"), CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.False(pending.IsCompleted);

        using var busy = await pending;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(5), "The read answered before the 5 second wait ended.");
        Assert.Equal("1", Assert.Single(busy.Headers.GetValues("Retry-After")));
        using (var problem = JsonDocument.Parse(await busy.Content.ReadAsStringAsync(CancellationToken)))
        {
            Assert.Equal("This tenant is busy. Try again in a moment.", problem.RootElement.GetProperty("title").GetString());
        }

        await PostgreSqlServer.AdminScalarAsync(platform, "DELETE FROM platform.tenant_leases", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request(Departments, "a.example.test"), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request(DepartmentNames, "a.example.test"), CancellationToken)).StatusCode);
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_leases", CancellationToken));
    }

    [Fact]
    public async Task In_a_cell_an_export_past_concurrentImports_answers_503_and_a_read_still_runs()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await SetQuotaAsync(platform, "a", QuotaKeys.ConcurrentImports, 1);
        await InsertLeaseAsync(platform, "a", "other", "60 seconds", QuotaKeys.ConcurrentImports);
        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var exporter = factory.CreateClient().SignedInAs("eve");

        foreach (var path in new[] { ItemExport, DataSourceExport })
        {
            using var busy = await exporter.SendAsync(Request(path, "a.example.test"), CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal("1", Assert.Single(busy.Headers.GetValues("Retry-After")));
            using var problem = JsonDocument.Parse(await busy.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("This tenant is busy. Try again in a moment.", problem.RootElement.GetProperty("title").GetString());
        }

        // The slot of the exports is not the slot of the reads.
        Assert.Equal(HttpStatusCode.OK, (await requester.SendAsync(Request(Departments, "a.example.test"), CancellationToken)).StatusCode);

        await PostgreSqlServer.AdminScalarAsync(platform, "DELETE FROM platform.tenant_leases", CancellationToken);
        Assert.Equal(HttpStatusCode.OK, (await exporter.SendAsync(Request(ItemExport, "a.example.test"), CancellationToken)).StatusCode);
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_leases", CancellationToken));
    }

    [Fact]
    public async Task In_a_cell_an_expired_lease_does_not_count_against_concurrentQueries()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await SetQuotaAsync(platform, "a", QuotaKeys.ConcurrentQueries, 1);
        await InsertLeaseAsync(platform, "a", "dead", "-1 second");
        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        using var client = factory.CreateClient().SignedInAs("requester");

        using var response = await client.SendAsync(Request(Departments, "a.example.test"), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_leases", CancellationToken));
    }

    [Fact]
    public async Task In_a_cell_a_read_runs_without_a_lease_and_logs_a_warning_while_the_platform_database_refuses_connections()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" });
        await SetQuotaAsync(platform, "a", QuotaKeys.ConcurrentQueries, 1);
        var logs = new LogCollector();
        await using var factory = CreateFactory(platform, logs, _cellSettings);
        using var client = factory.CreateClient().SignedInAs("requester");
        await CreateDepartmentAsync(client, "a.example.test", "Sales-a");

        await StopPlatformAsync(platform);
        try
        {
            await RefreshAsync(factory);

            Assert.Contains("Sales-a", await ListAsync(client, "a.example.test"), StringComparison.Ordinal);
            Assert.Contains(logs.Entries, entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("concurrentQueries lease", StringComparison.Ordinal));
        }
        finally
        {
            await AllowPlatformConnectionsAsync(platform);
        }
    }

    // Adds a lease of the tenant that ends at the database clock plus the interval, such as '60 seconds'.
    private static Task<object?> InsertLeaseAsync(string platform, string tenantId, string holder, string expiresIn, string quota = QuotaKeys.ConcurrentQueries) =>
        PostgreSqlServer.AdminScalarAsync(
            platform,
            $"INSERT INTO platform.tenant_leases (lease_id, tenant_id, quota, holder, expires_at) VALUES (gen_random_uuid(), '{tenantId}', '{quota}', '{holder}', now() + interval '{expiresIn}')",
            CancellationToken);

    [Fact]
    public async Task In_a_cell_a_tenant_over_its_storage_quota_cannot_write_records_but_another_tenant_can()
    {
        var role = NewRole();
        var a = await PrepareTenantAsync("a", role);
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(a with { Host = "a.example.test" }, b with { Host = "b.example.test" });
        await SetQuotaAsync(platform, "a", QuotaKeys.Storage, 1000);
        await using (var context = PlatformContext(platform))
        {
            context.TenantUsageLatest.Add(new TenantUsageLatestRow { TenantId = "a", DatabaseBytes = 2000, SampledAt = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync(CancellationToken);
        }

        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        using var client = factory.CreateClient().SignedInAs("requester");

        using var refused = Request(HttpMethod.Post, Departments, "a.example.test", """{ "values": { "name": "Sales-a" } }""");
        using var refusedResponse = await client.SendAsync(refused, CancellationToken);
        await CreateDepartmentAsync(client, "b.example.test", "Sales-b");

        using var problem = await ReadProblemAsync(refusedResponse, HttpStatusCode.Forbidden);
        Assert.Equal("This tenant has reached its storage quota.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("quota-exceeded", problem.RootElement.GetProperty("reason").GetString());
        Assert.Equal(1000, problem.RootElement.GetProperty("limit").GetInt64());
        Assert.Equal(2000, problem.RootElement.GetProperty("used").GetInt64());
        Assert.DoesNotContain("Sales-a", await ListAsync(client, "a.example.test"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_a_cell_a_changed_quota_row_is_seen_after_the_next_directory_refresh()
    {
        var role = NewRole();
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(b with { Host = "b.example.test" });
        await SetQuotaAsync(platform, "b", QuotaKeys.Users, 5);
        await using var factory = CreateFactory(platform, new LogCollector(), _cellSettings);
        var quotas = factory.Services.GetRequiredService<TenantQuotas>();
        Assert.Equal(5, quotas.Limit("b", QuotaKeys.Users));

        await SetQuotaAsync(platform, "b", QuotaKeys.Users, 7);

        Assert.Equal(5, quotas.Limit("b", QuotaKeys.Users));
        await RefreshAsync(factory);
        Assert.Equal(7, quotas.Limit("b", QuotaKeys.Users));
    }

    [Fact]
    public async Task Without_a_cell_every_quota_is_no_limit_even_for_a_tenant_with_rows()
    {
        var role = NewRole();
        var b = await PrepareTenantAsync("b", role);
        var platform = await PreparePlatformAsync(b with { Host = "b.example.test" });
        await SetQuotaAsync(platform, "b", QuotaKeys.Users, 5);
        await SetQuotaAsync(platform, "b", QuotaKeys.Applications, null);
        await using var factory = CreateFactory(platform, new LogCollector());
        var quotas = factory.Services.GetRequiredService<TenantQuotas>();

        var all = quotas.For("b");

        Assert.Equal(QuotaKeys.All, all.Keys);
        Assert.All(all.Values, value => Assert.Null(value));
    }

    private static Role NewRole() =>
        new($"dir_{Guid.NewGuid():N}"[..16], Convert.ToHexString(RandomNumberGenerator.GetBytes(12)));

    /// <summary>
    /// Creates a tenant database with both migrations applied and the RecordsApp fixture activated, then a login role
    /// that has the runtime grants and a random password.
    /// </summary>
    private async Task<DirectoryTenant> PrepareTenantAsync(string id, Role role)
    {
        var schemaConnection = await database.CreateDatabaseAsync($"directory_{id}", CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);

            var compiled = await ReleaseCompiler.CompileAsync(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordsApp"),
                configuration,
                CancellationToken);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(
                compiled,
                new ActiveReleaseStore(configuration),
                data,
                ActivationBindings.None("default"),
                OpenInstanceCounter.Instance,
                cancellationToken: CancellationToken);
            Assert.Empty(activated.Diagnostics);
        }

        // A role exists once per cluster, so the second tenant of a test reuses it.
        await PostgreSqlServer.AdminScalarAsync(
            schemaConnection,
            $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{role.Name}') THEN CREATE ROLE {role.Name} LOGIN PASSWORD '{role.Password}'; END IF; END $$",
            CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await RuntimeRoleGrants.ApplyAsync(connection, role.Name, CancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(schemaConnection);
        return new DirectoryTenant(id, $"{id}.example.test", builder.Host!, builder.Port, builder.Database!, role);
    }

    /// <summary>Creates a platform database with the directory tables and one active tenant row per tenant.</summary>
    private Task<string> PreparePlatformAsync(params DirectoryTenant[] tenants) => PreparePlatformAsync([], tenants);

    /// <summary>Creates a platform database with the directory tables, one active tenant row per tenant and the given bindings.</summary>
    private async Task<string> PreparePlatformAsync(TenantBindingRow[] bindings, params DirectoryTenant[] tenants)
    {
        var platform = await database.CreateDatabaseAsync("platform", CancellationToken);
        await using (var context = PlatformContext(platform))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await AddTenantsAsync(platform, tenants, bindings);
        return platform;
    }

    private static Task AddTenantsAsync(string platform, params DirectoryTenant[] tenants) => AddTenantsAsync(platform, tenants, []);

    private static async Task AddTenantsAsync(string platform, DirectoryTenant[] tenants, TenantBindingRow[] bindings)
    {
        await using var context = PlatformContext(platform);
        foreach (var tenant in tenants)
        {
            context.Tenants.Add(new TenantRow
            {
                Id = tenant.Id,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = tenant.DatabaseHost,
                DatabasePort = tenant.DatabasePort,
                DatabaseName = tenant.DatabaseName,
                RuntimeUser = tenant.Role.Name,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.Role.Password),
                SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = tenant.OidcAuthority,
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = tenant.Host });
        }

        context.TenantBindings.AddRange(bindings);
        await context.SaveChangesAsync(CancellationToken);
    }

    private static async Task UpdateTenantAsync(string platform, string tenantId, Action<TenantRow> update)
    {
        await using var context = PlatformContext(platform);
        var row = await context.Tenants.SingleAsync(tenant => tenant.Id == tenantId, CancellationToken);
        update(row);
        await context.SaveChangesAsync(CancellationToken);
    }

    private static Task SetRuntimeRoleAsync(string platform, string tenantId, Role role) =>
        UpdateTenantAsync(platform, tenantId, row =>
        {
            row.RuntimeUser = role.Name;
            row.RuntimePassword = _cipher.Encrypt(tenantId, TenantSecretFields.RuntimePassword, role.Password);
        });

    private static readonly (string Key, string Value)[] _cellSettings =
    [
        ("Cell:Id", "vn-1"),
        ("Cell:Region", "vn"),
        ("Cell:TenantDomain", "vn.example.test"),
    ];

    // Adds the tenant's row for the quota, or changes its value.
    private static async Task SetQuotaAsync(string platform, string tenantId, string quota, long? value)
    {
        await using var context = PlatformContext(platform);
        var row = await context.TenantQuotas.SingleOrDefaultAsync(q => q.TenantId == tenantId && q.Quota == quota, CancellationToken);
        if (row is null)
        {
            context.TenantQuotas.Add(new TenantQuotaRow { TenantId = tenantId, Quota = quota, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await context.SaveChangesAsync(CancellationToken);
    }

    private static PlatformDbContext PlatformContext(string platform) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);

    /// <summary>Reads the directory once now, as the timer does, and waits for the swap.</summary>
    private static Task RefreshAsync(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

    private static async Task CreateDepartmentAsync(HttpClient client, string host, string name)
    {
        using var request = Request(HttpMethod.Post, Departments, host, $$"""{ "values": { "name": "{{name}}" } }""");
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // The platform database cannot be stopped, since every test class shares one PostgreSQL container.
    // Refusing connections and ending the open sessions looks the same to the server. The statements run on the
    // fixture database, not on the platform database.
    private async Task StopPlatformAsync(string platform)
    {
        var name = new NpgsqlConnectionStringBuilder(platform).Database!;
        await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS false""", CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(
            database.ConnectionString, $"SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = '{name}'", CancellationToken);
    }

    private async Task AllowPlatformConnectionsAsync(string platform)
    {
        var name = new NpgsqlConnectionStringBuilder(platform).Database!;
        await PostgreSqlServer.AdminScalarAsync(database.ConnectionString, $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS true""", CancellationToken.None);
    }

    // Health answers bypass tenant resolution, so any host works.
    private static async Task<(HttpStatusCode Status, string Body)> ReadyAsync(HttpClient client)
    {
        using var response = await client.SendAsync(Request("/health/ready", "a.example.test"), CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<string> ListAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request(Departments, host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(CancellationToken);
    }

    private sealed record Role(string Name, string Password);

    private sealed record DirectoryTenant(string Id, string Host, string DatabaseHost, int DatabasePort, string DatabaseName, Role Role, string OidcAuthority = "https://127.0.0.1:1/realms/axis")
    {
        public string RuntimeConnectionString() =>
            new NpgsqlConnectionStringBuilder
            {
                Host = DatabaseHost,
                Port = DatabasePort,
                Database = DatabaseName,
                Username = Role.Name,
                Password = Role.Password,
            }.ConnectionString;
    }
}
