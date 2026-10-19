using System.Net;
using Axis.Processes.Storage;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class AdminMigrateTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string OldVersion = "20000101000000_Old";

    private static readonly string[] _steps = ["grants", "migrations", "grants", "role-check", "version"];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrate_with_parallel_2_migrates_the_tenants_that_can_be_reached_and_exits_1_for_the_one_that_cannot()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var ids = new[] { scenario.NewTenantId(), scenario.NewTenantId(), scenario.NewTenantId() };
        foreach (var id in ids)
        {
            Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
            await scenario.SetSchemaVersionAsync(id, OldVersion);
        }

        var broken = ids[1];
        await PostgreSqlServer.AdminScalarAsync(scenario.Platform, $"ALTER ROLE \"axis_{broken}_schema\" NOLOGIN", CancellationToken);

        var result = await scenario.RunAsync(["migrate", "--parallel", "2"]);

        Assert.Equal(1, result.Exit);
        foreach (var id in ids.Where(id => id != broken))
        {
            Assert.Equal(TenantSchemaVersion.Current, (await scenario.RowAsync(id)).SchemaVersion);
            Assert.Equal(_steps.Select(step => $"{id} {step} ok"), result.Output.Where(line => line.StartsWith($"{id} ", StringComparison.Ordinal)));
        }

        Assert.Equal(OldVersion, (await scenario.RowAsync(broken)).SchemaVersion);
        var failed = Assert.Single(result.Output, line => line.StartsWith($"{broken} ", StringComparison.Ordinal));
        Assert.StartsWith($"{broken} grants failed: ", failed, StringComparison.Ordinal);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_skips_provisioning_tenants_migrates_suspended_ones_and_keeps_every_status()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var active = scenario.NewTenantId();
        var suspended = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(active, $"{active}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(suspended, $"{suspended}.example.test")).Exit);
        await StopCreateAsync(scenario, provisioning);
        foreach (var id in new[] { active, suspended })
        {
            await scenario.SetSchemaVersionAsync(id, OldVersion);
        }

        await scenario.SetStatusAsync(suspended, "suspended");
        var before = await scenario.RowAsync(provisioning);

        var result = await scenario.RunAsync(["migrate"]);

        Assert.Equal(0, result.Exit);
        Assert.DoesNotContain(result.Output, line => line.StartsWith($"{provisioning} ", StringComparison.Ordinal));
        var activeRow = await scenario.RowAsync(active);
        var suspendedRow = await scenario.RowAsync(suspended);
        Assert.Equal((TenantSchemaVersion.Current, "active"), (activeRow.SchemaVersion, activeRow.Status));
        Assert.Equal((TenantSchemaVersion.Current, "suspended"), (suspendedRow.SchemaVersion, suspendedRow.Status));
        var after = await scenario.RowAsync(provisioning);
        Assert.Equal((before.Version, before.Status, before.SchemaVersion), (after.Version, after.Status, after.SchemaVersion));

        // The tenants go in id order.
        var order = result.Output.Select(line => line.Split(' ')[0]).Distinct().ToArray();
        Assert.Equal(order.Order(StringComparer.Ordinal), order);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_with_a_tenant_option_migrates_only_that_tenant()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var one = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(one, $"{one}.example.test")).Exit);
        Assert.Equal(0, (await scenario.CreateAsync(other, $"{other}.example.test")).Exit);
        await scenario.SetSchemaVersionAsync(one, OldVersion);
        await scenario.SetSchemaVersionAsync(other, OldVersion);

        var result = await scenario.RunAsync(["migrate", "--tenant", one]);

        Assert.Equal(0, result.Exit);
        Assert.Equal(TenantSchemaVersion.Current, (await scenario.RowAsync(one)).SchemaVersion);
        Assert.Equal(OldVersion, (await scenario.RowAsync(other)).SchemaVersion);
        Assert.All(result.Output, line => Assert.StartsWith($"{one} ", line, StringComparison.Ordinal));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_server_under_directory_answers_503_for_a_tenant_on_an_older_schema_until_migrate_has_run_and_the_directory_is_read_again()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var host = $"{id}.example.test";
        Assert.Equal(0, (await scenario.CreateAsync(id, host)).Exit);
        await scenario.SetSchemaVersionAsync(id, OldVersion);
        await using var factory = CreateFactory(scenario.Platform);
        using var client = factory.CreateClient().SignedInAs("anna");

        using var held = await client.SendAsync(Request("/api/me", host), CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, held.StatusCode);

        var migrated = await scenario.RunAsync(["migrate"]);
        Assert.Equal(0, migrated.Exit);
        await factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);

        using var served = await client.SendAsync(Request("/api/me", host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_for_an_unknown_or_provisioning_tenant_or_with_a_bad_option_exits_2_and_changes_no_directory_row()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var active = scenario.NewTenantId();
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(active, $"{active}.example.test")).Exit);
        await StopCreateAsync(scenario, provisioning);
        await scenario.SetSchemaVersionAsync(active, OldVersion);
        var before = await scenario.SnapshotAsync();

        var unknown = await scenario.RunAsync(["migrate", "--tenant", "nobody"]);
        var notReady = await scenario.RunAsync(["migrate", "--tenant", provisioning]);
        var zero = await scenario.RunAsync(["migrate", "--parallel", "0"]);
        var text = await scenario.RunAsync(["migrate", "--parallel", "many"]);
        var repeated = await scenario.RunAsync(["migrate", "--tenant", active, "--tenant", active]);
        var option = await scenario.RunAsync(["migrate", "--all"]);

        Assert.All([unknown, notReady, zero, text, repeated, option], result =>
        {
            Assert.Equal(2, result.Exit);
            Assert.Empty(result.Output);
        });
        Assert.Contains("does not exist", unknown.Error[0], StringComparison.Ordinal);
        Assert.Contains("provisioning", notReady.Error[0], StringComparison.Ordinal);
        Assert.Contains("--parallel", zero.Error[0], StringComparison.Ordinal);
        Assert.StartsWith("Usage:", text.Error[0], StringComparison.Ordinal);
        Assert.Equal(before, await scenario.SnapshotAsync());

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_before_the_platform_tables_exist_does_nothing_and_exits_0()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: false, CancellationToken);

        var result = await scenario.RunAsync(["migrate"]);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains("platform tables do not exist", result.Error[0], StringComparison.Ordinal);

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_needs_neither_the_admin_role_nor_the_client_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);

        var result = await scenario.RunAsync(["migrate"], adminConnectionString: "", withClientSecret: false);

        Assert.Equal(0, result.Exit);
        Assert.Empty(result.Output);

        await scenario.AssertNoSecretsAsync();
    }

    // Stops a create right after its entry is written, which leaves a provisioning tenant and no database.
    private static async Task StopCreateAsync(AdminScenario scenario, string id)
    {
        using var stop = new CancellationTokenSource();
        var result = await scenario.RunAsync(AdminScenario.CreateArgs(id, $"{id}.example.test"), new CancelAfterEntryWriter(stop), stop);
        Assert.Equal(1, result.Exit);
        Assert.Equal("provisioning", (await scenario.RowAsync(id)).Status);
    }

    private static WebApplicationFactory<Program> CreateFactory(string platform) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Tenancy:Source", "directory");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(platform));
            builder.UseSetting("ConnectionStrings:PlatformSchema", platform);
            builder.UseSetting("Platform:SecretKey", AdminScenario.TestSecretKey);

            // An hour, so the timer never races the refresh that the test triggers itself.
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna Employee", "employee"));
        });

    private sealed class CancelAfterEntryWriter(CancellationTokenSource source) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            if (value is not null && value.EndsWith(" entry ok", StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }
}
