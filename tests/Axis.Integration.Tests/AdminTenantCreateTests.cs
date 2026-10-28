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
            ["entry", "roles", "database", "grants", "migrations", "grants", "role-check", "version"],
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
    public async Task Create_without_the_client_secret_or_with_a_wrong_shape_exits_2()
    {
        await using var scenario = await NewScenarioAsync();
        var id = scenario.NewTenantId();

        var noSecret = await scenario.RunAsync(AdminScenario.CreateArgs(id, "shop.example.test"), withClientSecret: false);
        var noHost = await scenario.RunAsync(["tenant", "create", id, "--oidc-authority", AdminScenario.Authority, "--oidc-client-id", "axis"]);
        var unknown = await scenario.RunAsync(["tenant", "delete", id]);

        Assert.Equal(2, noSecret.Exit);
        Assert.Contains("AXIS_OIDC_CLIENT_SECRET", noSecret.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, noHost.Exit);
        Assert.StartsWith("Usage:", noHost.Error[0], StringComparison.Ordinal);
        Assert.Equal(2, unknown.Exit);
        Assert.Equal(0L, await scenario.CountEntriesAsync());

        await scenario.AssertNoSecretsAsync();
    }

    private Task<AdminScenario> NewScenarioAsync(bool migrate = true) => AdminScenario.NewAsync(database, migrate, CancellationToken);
}
