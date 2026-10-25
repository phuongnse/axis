using Axis.Configuration.Tests;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class AdminPlatformAuditTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string UrlBinding = "PurchasingSystemUrl";
    private const string SecretBinding = "PurchasingSystemToken";

    private static readonly string _sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_of_the_seven_commands_writes_one_record_with_the_command_tenant_user_and_machine_and_no_secret()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        using var tools = await PostgresClientTools.CreateAsync();
        var id = scenario.NewTenantId();
        var restored = scenario.NewTenantId();
        using var folder = new TemporaryFolder();
        var file = Path.Combine(folder.Path, "backup.tar");

        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await AddBindingsAsync(scenario, id);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "suspend", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "resume", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["migrate", "--tenant", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _sample, "--tenant", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "backup", id, "--out", file], path: tools.PathSetting)).Exit);
        Assert.Equal(
            0,
            (await scenario.RunAsync(["tenant", "restore", file, "--tenant", restored, "--host", $"{restored}.example.test"], path: tools.PathSetting)).Exit);

        var records = await RecordsAsync(scenario);
        Assert.Equal(
            [
                ("activate", id),
                ("migrate", id),
                ("tenant backup", id),
                ("tenant create", id),
                ("tenant restore", restored),
                ("tenant resume", id),
                ("tenant suspend", id),
            ],
            records.Select(record => (record.Command, record.TenantId)).Order());
        Assert.All(records, record =>
        {
            Assert.Equal(Environment.UserName, record.OsUser);
            Assert.Equal(Environment.MachineName, record.Machine);
            Assert.Empty(record.Changed);
            Assert.DoesNotContain(scenario.ClientSecret, record.Text, StringComparison.Ordinal);
        });

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Tenant_list_and_version_write_no_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = (await RecordsAsync(scenario)).Count;

        Assert.Equal(0, (await scenario.RunAsync(["tenant", "list"])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["version"])).Exit);

        Assert.Equal(before, (await RecordsAsync(scenario)).Count);
    }

    [Fact]
    public async Task A_usage_error_an_unknown_tenant_and_a_held_host_exit_2_and_write_no_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        var other = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        var before = (await RecordsAsync(scenario)).Count;

        var usage = await scenario.RunAsync(["tenant", "suspend"]);
        var unknownSuspend = await scenario.RunAsync(["tenant", "suspend", "t-unknown"]);
        var unknownMigrate = await scenario.RunAsync(["migrate", "--tenant", "t-unknown"]);
        var held = await scenario.CreateAsync(other, $"{id}.example.test");

        Assert.All([usage, unknownSuspend, unknownMigrate, held], result => Assert.Equal(2, result.Exit));
        Assert.StartsWith("Usage:", usage.Error[0], StringComparison.Ordinal);
        Assert.Equal(["The tenant does not exist."], unknownSuspend.Error);
        Assert.Equal($"{other} entry failed: a host belongs to another tenant", Assert.Single(held.Output));
        Assert.Equal(before, (await RecordsAsync(scenario)).Count);
    }

    [Fact]
    public async Task A_record_that_cannot_be_written_stops_the_tenant_with_an_audit_line_and_changes_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await PostgreSqlServer.AdminScalarAsync(
            scenario.Platform,
            """
            CREATE FUNCTION platform.reject_audit_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'audit is down';
            END;
            $$;
            CREATE TRIGGER audit_records_no_insert BEFORE INSERT ON platform.audit_records
                FOR EACH ROW EXECUTE FUNCTION platform.reject_audit_insert();
            """,
            CancellationToken);
        var version = (await scenario.RowAsync(id)).Version;

        var result = await scenario.RunAsync(["tenant", "suspend", id]);

        Assert.Equal(1, result.Exit);
        Assert.Equal([$"{id} audit failed: audit is down"], result.Output);
        var row = await scenario.RowAsync(id);
        Assert.Equal(("active", version), (row.Status, row.Version));

        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Migrate_creates_the_audit_table_on_an_older_platform_database_and_writes_its_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            await context.GetService<IMigrator>().MigrateAsync("20261020024803_AddDataProtectionKeys", CancellationToken);
        }

        Assert.Null(await PostgreSqlServer.AdminScalarAsync(scenario.Platform, "SELECT to_regclass('platform.audit_records')::text", CancellationToken));

        var result = await scenario.RunAsync(["migrate"]);

        Assert.Equal(0, result.Exit);
        Assert.NotNull(await PostgreSqlServer.AdminScalarAsync(scenario.Platform, "SELECT to_regclass('platform.audit_records')::text", CancellationToken));
        var record = Assert.Single(await RecordsAsync(scenario));
        Assert.Equal(("migrate", id), (record.Command, record.TenantId));

        await scenario.AssertNoSecretsAsync();
    }

    private static async Task AddBindingsAsync(AdminScenario scenario, string id)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options);
        context.TenantBindings.AddRange(
            new TenantBindingRow { TenantId = id, Name = UrlBinding, Kind = "url", Url = "https://purchasing.example.test" },
            new TenantBindingRow
            {
                TenantId = id,
                Name = SecretBinding,
                Kind = "secret",
                SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding(SecretBinding), "binding-secret-value"),
            });
        await context.SaveChangesAsync(CancellationToken);
    }

    private static async Task<List<AuditRow>> RecordsAsync(AdminScenario scenario)
    {
        await using var connection = new NpgsqlConnection(scenario.Platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT command, tenant_id, os_user, machine, changed, t::text FROM platform.audit_records t ORDER BY occurred_at, id",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<string[]>(4),
                reader.GetString(5)));
        }

        return rows;
    }

    private sealed record AuditRow(string Command, string? TenantId, string OsUser, string Machine, string[] Changed, string Text);
}
