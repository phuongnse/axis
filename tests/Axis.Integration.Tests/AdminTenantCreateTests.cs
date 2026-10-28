using System.Net;
using System.Security.Cryptography;
using Axis.Admin;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminTenantCreateTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_makes_a_tenant_that_the_server_serves_and_whose_runtime_role_cannot_create_tables()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var result = await scenario.CreateAsync(id, "Shop.Example.Test");

        Assert.Equal(0, result.Exit);
        Assert.Equal(
            ["entry", "realm", "roles", "database", "grants", "migrations", "grants", "role-check", "version"],
            result.Output.Select(line => line.Split(' ')[1]));
        Assert.All(result.Output, line => Assert.EndsWith(" ok", line, StringComparison.Ordinal));
        var row = await scenario.RowAsync(id);
        Assert.Equal("active", row.Status);
        Assert.NotEmpty(row.SchemaVersion);
        Assert.Equal($"axis_{id}", row.DatabaseName);

        await using (var factory = AdminScenario.CreateServer(scenario.Platform))
        {
            using var client = factory.CreateClient().SignedInAs("requester");
            using var served = await client.SendAsync(Request("/api/me", "shop.example.test"), CancellationToken);
            using var unknown = await client.SendAsync(Request("/api/me", "other.example.test"), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        var runtime = AdminScenario.Cipher.Decrypt(id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.RuntimeUser, runtime));
        await connection.OpenAsync(CancellationToken);
        await using var create = new NpgsqlCommand("CREATE TABLE entities.x(i int)", connection);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync(CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

        // Another login role of the cluster cannot connect to the tenant database.
        await using var other = new NpgsqlConnection(
            scenario.TenantConnectionString(id, PostgreSqlServer.RuntimeRole, PostgreSqlServer.RolePassword));
        var refused = await Assert.ThrowsAsync<PostgresException>(() => other.OpenAsync(CancellationToken));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_installs_the_pg_trgm_extension_in_the_tenant_database()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";

        var result = await scenario.CreateAsync(id, host);

        Assert.Equal(0, result.Exit);
        var row = await scenario.RowAsync(id);
        var schema = await PostgreSqlServer.AdminScalarAsync(
            scenario.TenantConnectionString(id, row.SchemaUser, ""),
            "SELECT extnamespace::regnamespace::text FROM pg_extension WHERE extname = 'pg_trgm'",
            CancellationToken);
        Assert.Equal("public", schema);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_create_stopped_after_the_database_exists_stays_provisioning_and_a_rerun_finishes_it_with_the_same_passwords()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        var output = new CancelOnLineWriter(stop);

        var first = await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), output, stop);

        Assert.Equal(1, first.Exit);
        Assert.Contains($"{id} database ok", first.Output);
        Assert.Contains($"{id} grants failed: canceled", first.Output);
        var stopped = await scenario.RowAsync(id);
        Assert.Equal("provisioning", stopped.Status);
        Assert.True(await scenario.DatabaseExistsAsync(id));

        var second = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(0, second.Exit);
        var finished = await scenario.RowAsync(id);
        Assert.Equal("active", finished.Status);
        Assert.NotEmpty(finished.SchemaVersion);
        Assert.Equal(stopped.SchemaPassword, finished.SchemaPassword);
        Assert.Equal(stopped.RuntimePassword, finished.RuntimePassword);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_rerun_on_a_provisioning_entry_updates_its_hosts_and_never_its_passwords()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        using var stop = new CancellationTokenSource();
        await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.exampel.test"), new CancelOnLineWriter(stop), stop);
        var stopped = await scenario.RowAsync(id);

        var rerun = await scenario.CreateAsync(id, "SHOP.example.test");

        Assert.Equal(0, rerun.Exit);
        Assert.Equal(["SHOP.example.test"], await scenario.HostsAsync(id));
        var finished = await scenario.RowAsync(id);
        Assert.Equal(stopped.SchemaPassword, finished.SchemaPassword);
        Assert.Equal(stopped.RuntimePassword, finished.RuntimePassword);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_for_an_active_tenant_exits_1_and_changes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, "shop.example.test")).Exit);
        var before = await scenario.SnapshotAsync();

        var again = await scenario.CreateAsync(id, "other.example.test");

        Assert.Equal(1, again.Exit);
        Assert.Equal([$"{id} entry failed: the tenant already exists"], again.Output);
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("shop.example.test")]
    [InlineData("SHOP.Example.TEST")]
    public async Task Create_with_a_host_of_another_tenant_exits_2_and_creates_nothing(string conflicting)
    {
        await using var scenario = await NewScenarioAsync();
        var first = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(first, "shop.example.test")).Exit);
        var before = await scenario.SnapshotAsync();
        var second = scenario.NewTenantId();

        var result = await scenario.CreateAsync(second, "free.example.test", conflicting);

        Assert.Equal(2, result.Exit);
        Assert.Equal([$"{second} entry failed: a host belongs to another tenant"], result.Output);
        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.False(await scenario.DatabaseExistsAsync(second));
        Assert.Equal(0L, await scenario.CountRolesAsync(second));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_with_a_superuser_admin_role_exits_2_and_writes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var superuser = await PostgreSqlServer.AdminConnectionStringAsync();

        var result = await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), adminConnectionString: superuser);

        Assert.Equal(2, result.Exit);
        Assert.Contains("superuser", result.Error[0], StringComparison.Ordinal);
        Assert.Empty(result.Output);
        Assert.Equal(0L, await scenario.CountEntriesAsync());
        Assert.False(await scenario.DatabaseExistsAsync(id));
        Assert.Equal(0L, await scenario.CountRolesAsync(id));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_with_an_admin_role_that_lacks_CREATEROLE_exits_2_and_writes_nothing()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var limited = await scenario.NewAdminRoleAsync("CREATEDB");

        var result = await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), adminConnectionString: limited);

        Assert.Equal(2, result.Exit);
        Assert.Contains("CREATEDB and CREATEROLE", result.Error[0], StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_before_the_platform_tables_exist_exits_2()
    {
        await using var scenario = await NewScenarioAsync(migrate: false);
        var id = scenario.NewTenantId();

        var result = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(2, result.Exit);
        Assert.Contains("platform tables do not exist", result.Error[0], StringComparison.Ordinal);
        Assert.False(await scenario.DatabaseExistsAsync(id));

        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("under_score")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Create_with_an_invalid_id_exits_2(string id)
    {
        await using var scenario = await NewScenarioAsync();

        var result = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(2, result.Exit);
        Assert.Contains("tenant id", result.Error[0], StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Create_without_the_identity_provider_url_or_with_a_wrong_shape_exits_2()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var noUrl = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, "shop.example.test"),
            extra: new Dictionary<string, string?> { ["IdentityProvider:Url"] = null });
        var noHost = await scenario.RunAsync(["tenant", "create", id]);
        var oidcAuthority = await scenario.RunAsync([.. AdminScenario.CreateArgs(id, "shop.example.test"), "--oidc-authority", AdminScenario.Authority]);
        var oidcClientId = await scenario.RunAsync([.. AdminScenario.CreateArgs(id, "shop.example.test"), "--oidc-client-id", "axis"]);
        var unknown = await scenario.RunAsync(["tenant", "delete", id]);

        Assert.Equal(2, noUrl.Exit);
        Assert.Contains("IdentityProvider:Url", noUrl.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noHost.Exit);
        Assert.StartsWith("Usage:", noHost.Error[0], StringComparison.Ordinal);
        Assert.All(new[] { oidcAuthority, oidcClientId }, result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.StartsWith("Usage:", result.Error[0], StringComparison.Ordinal);
        });
        Assert.Equal(2, unknown.Exit);
        Assert.Equal(0L, await scenario.CountEntriesAsync());
        Assert.Empty(scenario.IdentityProvider.Paths);

        await scenario.AssertNoSecretsAsync();
    }

    [Theory]
    [InlineData("IdentityProvider:AdminClientId", null)]
    [InlineData("IdentityProvider:AdminClientSecret", null)]
    [InlineData("IdentityProvider:Url", "not a url")]
    [InlineData("IdentityProvider:Url", "ftp://idp.example.test/")]
    public async Task Create_with_a_missing_or_invalid_identity_provider_setting_exits_2_naming_the_setting(string setting, string? value)
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var result = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, "shop.example.test"),
            extra: new Dictionary<string, string?> { [setting] = value });

        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(setting, Assert.Single(result.Error), StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());
        Assert.Empty(scenario.IdentityProvider.Paths);
    }

    [Fact]
    public async Task Create_refuses_the_tenant_id_master_because_the_identity_provider_owns_that_realm()
    {
        await using var scenario = await NewScenarioAsync();

        var result = await scenario.CreateAsync("master", "shop.example.test");

        Assert.Equal(2, result.Exit);
        Assert.Contains("master", Assert.Single(result.Error), StringComparison.Ordinal);
        Assert.Equal(0L, await scenario.CountEntriesAsync());
        Assert.Empty(scenario.IdentityProvider.Paths);
    }

    [Fact]
    public async Task Create_outside_development_and_testing_refuses_a_plain_http_identity_provider_address()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var result = await scenario.RunAsync(
            AdminScenario.CreateArgs(id, "shop.example.test"),
            extra: new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["Admin:ConnectionString"] = new NpgsqlConnectionStringBuilder(scenario.AdminConnection) { SslMode = SslMode.VerifyFull }.ConnectionString,
                ["ConnectionStrings:PlatformSchema"] = new NpgsqlConnectionStringBuilder(scenario.Platform) { SslMode = SslMode.VerifyFull }.ConnectionString,
            });

        Assert.Equal(2, result.Exit);
        Assert.Contains("IdentityProvider:Url", Assert.Single(result.Error), StringComparison.Ordinal);
        Assert.Contains("https", result.Error[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rerun_after_a_stop_at_the_realm_step_reuses_the_stored_secrets_and_finishes()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();
        var failing = await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), identityProvider: new FailingHandler());

        Assert.Equal(1, failing.Exit);
        Assert.Equal([$"{id} entry ok"], failing.Output.Take(1));
        Assert.Contains($"{id} realm failed", failing.Output[1], StringComparison.Ordinal);
        var stopped = await scenario.RowAsync(id);
        Assert.Equal("provisioning", stopped.Status);
        Assert.Equal(AdminScenario.RealmAuthority(id), stopped.OidcAuthority);
        Assert.Equal("axis", stopped.OidcClientId);
        Assert.NotEmpty(stopped.OidcClientSecret);
        Assert.NotEmpty(stopped.RealmActivationClientSecret!);
        Assert.NotEmpty(stopped.RealmUsersClientSecret!);

        var rerun = await scenario.CreateAsync(id, "shop.example.test");

        Assert.Equal(0, rerun.Exit);
        var done = await scenario.RowAsync(id);
        Assert.Equal("active", done.Status);
        Assert.Equal(stopped.OidcClientSecret, done.OidcClientSecret);
        Assert.Equal(stopped.RealmActivationClientSecret, done.RealmActivationClientSecret);
        Assert.Equal(stopped.RealmUsersClientSecret, done.RealmUsersClientSecret);
        await scenario.AssertNoSecretsAsync();
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    private Task<AdminScenario> NewScenarioAsync(bool migrate = true) => AdminScenario.NewAsync(database, migrate, CancellationToken);
}
