using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Core.Tenancy;
using Axis.Data.Files;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.Polling;
using TenantSettings = Axis.Tenancy.TenantSettings;

namespace Axis.Integration.Tests;

/// <summary>
/// In a cell the worker measures the storage of each tenant every hour, and counts the work items it runs, as the
/// tenant's usage in the platform database. A worker outside a cell writes none. The same hourly pass brings the users quota
/// step of each tenant's realm in step with its <c>users</c> quota.
/// </summary>
public sealed class WorkerTenantUsageTests(PostgreSqlFixture database, KeycloakFixture keycloak) : IClassFixture<PostgreSqlFixture>
{
    private const string RecordKind = "test.record";
    private const string TestSecretKey = TestPlatform.SecretKey;
    private const int FileSize = 1_000_000;

    private static readonly (string Key, string Value)[] _cellSettings =
    [
        ("Tenancy:Source", "directory"),
        ("Cell:Id", "vn-1"),
        ("Cell:Region", "vn"),
        ("Cell:TenantDomain", "vn.example.test"),
    ];

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_worker_in_a_cell_writes_the_latest_row_and_the_days_row_with_database_bytes_and_file_bytes()
    {
        var clock = new ManualClock();
        var tenant = await PrepareTenantAsync("a");
        await InsertFileAsync(tenant);
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        await using var app = BuildWorker(platform, clock, _cellSettings);

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await LatestAsync(platform, "a") is not null, "The worker did not sample the tenant.");

        var latest = (await LatestAsync(platform, "a"))!.Value;
        Assert.True(latest.FileBytes >= FileSize, $"{latest.FileBytes} file bytes");
        Assert.True(latest.DatabaseBytes > 0, $"{latest.DatabaseBytes} database bytes");
        Assert.Equal(clock.GetUtcNow(), latest.SampledAt);
        var day = (await DayAsync(platform, "a", clock.Today)).Single();
        Assert.Equal(latest.DatabaseBytes, day.DatabaseBytes);
        Assert.Equal(latest.FileBytes, day.FileBytes);
        Assert.Equal((0L, 0L, 0), (day.ConnectorCalls, day.WorkItemsRun, day.EmailsSent));
        Assert.Null(day.Users);
        Assert.Null(day.Applications);
    }

    [Fact]
    public async Task A_second_sample_the_same_day_replaces_the_latest_row_and_the_day_keeps_the_larger_total()
    {
        var clock = new ManualClock();
        var tenant = await PrepareTenantAsync("a");
        await InsertFileAsync(tenant);
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        var larger = 1L << 40;
        await PostgreSqlServer.AdminScalarAsync(
            platform,
            $"INSERT INTO platform.tenant_usage (tenant_id, day, database_bytes, file_bytes) VALUES ('a', '{clock.Today:yyyy-MM-dd}', {larger}, 7)",
            CancellationToken);
        await using var app = BuildWorker(platform, clock, _cellSettings);

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await LatestAsync(platform, "a") is not null, "The worker did not sample the tenant.");

        var first = (await LatestAsync(platform, "a"))!.Value;
        Assert.True(first.DatabaseBytes < larger);
        Assert.Equal((larger, 7L), DayTotals((await DayAsync(platform, "a", clock.Today)).Single()));

        // The next sample is due one hour later, and the tenant holds more by then.
        await InsertFileAsync(tenant);
        clock.Advance(TimeSpan.FromHours(1));
        await WaitUntilAsync(async () => (await LatestAsync(platform, "a"))!.Value.SampledAt == clock.GetUtcNow(), "The worker did not sample again.");

        var second = (await LatestAsync(platform, "a"))!.Value;
        Assert.True(second.FileBytes > first.FileBytes, $"{second.FileBytes} file bytes after {first.FileBytes}");
        Assert.Equal((larger, 7L), DayTotals((await DayAsync(platform, "a", clock.Today)).Single()));
    }

    [Fact]
    public async Task A_sample_keeps_the_larger_of_two_samples_of_a_day_whichever_comes_first()
    {
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, await PrepareTenantAsync("a"));
        var store = new TenantUsageStore(NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform)));
        await GrantPlatformAsync(platform);
        var now = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);

        await store.RecordSampleAsync("a", 100, 50, now, CancellationToken);
        await store.RecordSampleAsync("a", 10, 5, now.AddHours(1), CancellationToken);
        Assert.Equal((100L, 50L), DayTotals((await DayAsync(platform, "a", DateOnly.FromDateTime(now.UtcDateTime))).Single()));
        Assert.Equal((10L, 5L), (await LatestAsync(platform, "a"))!.Value.Totals);

        await store.RecordSampleAsync("a", 200, 60, now.AddHours(2), CancellationToken);
        Assert.Equal((200L, 60L), DayTotals((await DayAsync(platform, "a", DateOnly.FromDateTime(now.UtcDateTime))).Single()));
    }

    [Fact]
    public async Task A_connector_call_and_another_work_item_add_to_the_days_counts()
    {
        var tenant = await PrepareTenantAsync("a");
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        await GrantPlatformAsync(platform);
        var clock = new ManualClock();
        var usage = new CellWorkItemUsage(new TenantUsageStore(NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform))), clock);
        var options = new TenantOptions();
        options.Tenants["a"] = new TenantSettings
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(tenant.SchemaConnectionString) { Username = tenant.Role, Password = tenant.Password }.ConnectionString,
        };
        var accessor = new TenantContextAccessor();
        await using var pools = new TenantConnectionFactory(ConfigurationTenantCatalog.FromOptions(options), accessor, TenantPoolSettings.Default);
        var runner = new WorkItemRunner(
            pools,
            accessor,
            [new NothingHandler()],
            NullLogger<WorkItemRunner>.Instance,
            [new DeletingExternalHandler()],
            usage: usage);
        await EnqueueAsync(tenant, ProcessStarts.ConnectorCallWorkItemKind);
        await EnqueueAsync(tenant, RecordKind);

        Assert.True(await runner.RunNextAsync("a", TimeSpan.FromSeconds(30), CancellationToken));
        Assert.True(await runner.RunNextAsync("a", TimeSpan.FromSeconds(30), CancellationToken));

        var day = (await DayAsync(platform, "a", clock.Today)).Single();
        Assert.Equal((1L, 2L), (day.ConnectorCalls, day.WorkItemsRun));
    }

    [Fact]
    public async Task A_sample_deletes_the_tenants_day_rows_older_than_13_months_and_keeps_the_newer_ones()
    {
        var tenant = await PrepareTenantAsync("a");
        var other = await PrepareTenantAsync("b");
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant, other);
        await GrantPlatformAsync(platform);
        var now = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var older = today.AddMonths(-13).AddDays(-1);
        var newer = today.AddMonths(-13).AddDays(1);
        foreach (var (id, day) in new[] { ("a", older), ("a", newer), ("b", older) })
        {
            await PostgreSqlServer.AdminScalarAsync(
                platform,
                $"INSERT INTO platform.tenant_usage (tenant_id, day) VALUES ('{id}', '{day:yyyy-MM-dd}')",
                CancellationToken);
        }

        await new TenantUsageStore(NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform)))
            .RecordSampleAsync("a", 1, 1, now, CancellationToken);

        Assert.Equal([newer, today], (await DayAsync(platform, "a", null)).Select(row => row.Day).Order());

        // Only the sampled tenant is cleaned.
        Assert.Equal([older], (await DayAsync(platform, "b", null)).Select(row => row.Day));
    }

    [Fact]
    public async Task A_worker_without_a_cell_id_runs_its_work_and_writes_no_usage()
    {
        var tenant = await PrepareTenantAsync("a");
        await InsertFileAsync(tenant);
        var item = await EnqueueAsync(tenant, RecordKind);
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        await GrantPlatformAsync(platform);
        await using var app = BuildWorker(platform, new ManualClock(), []);

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(
            async () => (long)(await PostgreSqlServer.AdminScalarAsync(tenant.SchemaConnectionString, $"SELECT count(*) FROM test_runs WHERE item_id = '{item}'", CancellationToken))! == 1,
            "The work did not run.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);

        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_usage_latest", CancellationToken));
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_usage", CancellationToken));
    }

    [Fact]
    public async Task The_usage_tables_hold_no_text_json_bytes_or_array_column_but_the_tenant_id()
    {
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform);
        var columns = new List<(string Table, string Column, string Type)>();
        await using (var connection = new NpgsqlConnection(platform))
        {
            await connection.OpenAsync(CancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT table_name, column_name, data_type FROM information_schema.columns
                WHERE table_schema = 'platform' AND table_name IN ('tenant_usage', 'tenant_usage_latest')
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            while (await reader.ReadAsync(CancellationToken))
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        Assert.Equal(["tenant_usage", "tenant_usage_latest"], columns.Select(column => column.Table).Distinct().Order());
        Assert.Equal(13, columns.Count);
        string[] freeForm = ["text", "character varying", "character", "json", "jsonb", "bytea", "ARRAY", "USER-DEFINED"];
        Assert.All(columns.Where(column => freeForm.Contains(column.Type)), column => Assert.Equal("tenant_id", column.Column));
        Assert.All(columns.Where(column => column.Column == "tenant_id"), column => Assert.Equal("text", column.Type));
    }

    [Fact]
    public async Task The_platform_reader_role_writes_the_usage_tables_and_still_cannot_write_the_directory()
    {
        var tenant = await PrepareTenantAsync("a");
        var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        await GrantPlatformAsync(platform);
        await using var source = NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform));

        await Execute("INSERT INTO platform.tenant_usage_latest (tenant_id, database_bytes, file_bytes, sampled_at) VALUES ('a', 1, 1, now())");
        await Execute("INSERT INTO platform.tenant_usage (tenant_id, day) VALUES ('a', current_date)");
        await Execute("UPDATE platform.tenant_usage SET connector_calls = 1");
        await Execute("DELETE FROM platform.tenant_usage");
        await Execute("DELETE FROM platform.tenant_usage_latest");

        var denied = await Assert.ThrowsAsync<PostgresException>(
            () => Execute("INSERT INTO platform.tenant_quotas (tenant_id, quota, value) VALUES ('a', 'users', 1)"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        denied = await Assert.ThrowsAsync<PostgresException>(() => Execute("UPDATE platform.tenants SET status = 'suspended'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

        async Task Execute(string sql)
        {
            await using var command = source.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }
    }

    [Fact]
    public async Task The_hourly_pass_switches_the_users_quota_step_on_at_the_quota_and_off_when_it_is_raised_and_a_failing_realm_does_not_stop_it()
    {
        var clock = new ManualClock();
        var baseAddress = await keycloak.BaseAddressAsync();
        using var realms = await UpstreamRealms.CreateAsync(baseAddress);
        try
        {
            // The tenant "a" sorts before the realm's tenant, so the poll handles its failing identity provider first.
            var id = realms.NewRealmName();
            var secrets = new TenantRealmSecrets("platform-secret-1", "activation-secret-1", "users-secret-1");
            using var http = new HttpClient();
            await new TenantRealm(http, new IdentityProviderAdmin(baseAddress, KeycloakFixture.AdminClientId, KeycloakFixture.AdminClientSecret))
                .EnsureAsync(id, "Acme", secrets, new Dictionary<string, string>(), CancellationToken);
            await realms.AddUpstreamUserAsync(id);

            var failing = await PrepareTenantAsync("a");
            var good = await PrepareTenantAsync(id);
            var platform = await database.CreateDatabaseAsync("usage_platform", CancellationToken);
            await SeedPlatformAsync(platform, failing, good);
            await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options))
            {
                var row = await context.Tenants.SingleAsync(tenant => tenant.Id == id, CancellationToken);
                row.OidcAuthority = new Uri(baseAddress, $"realms/{id}").ToString();
                row.RealmActivationClientSecret = _cipher.Encrypt(id, TenantSecretFields.RealmActivationClientSecret, secrets.Activation);
                row.RealmUsersClientSecret = _cipher.Encrypt(id, TenantSecretFields.RealmUsersClientSecret, secrets.Users);
                var unreachable = await context.Tenants.SingleAsync(tenant => tenant.Id == "a", CancellationToken);
                unreachable.OidcAuthority = "http://127.0.0.1:1/realms/a";
                unreachable.RealmActivationClientSecret = _cipher.Encrypt("a", TenantSecretFields.RealmActivationClientSecret, "activation-secret-1");
                unreachable.RealmUsersClientSecret = _cipher.Encrypt("a", TenantSecretFields.RealmUsersClientSecret, "users-secret-1");
                await context.SaveChangesAsync(CancellationToken);
            }

            await PostgreSqlServer.AdminScalarAsync(platform, $"INSERT INTO platform.tenant_quotas (tenant_id, quota, value) VALUES ('{id}', 'users', 1)", CancellationToken);
            var logs = new LogCollector();
            await using var app = BuildWorker(platform, clock, logs, [.. _cellSettings, ("Tenancy:RefreshInterval", "00:01:00")]);

            await app.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await DenyStepAsync(realms, id) == "REQUIRED", "The worker did not switch the users quota step on.");

            Assert.Contains(
                logs.Records,
                record => record.Level == LogLevel.Warning && record.Message.Contains("users quota step of tenant a ", StringComparison.Ordinal));
            Assert.NotNull(await LatestAsync(platform, "a"));

            // The quota is raised above the user count. The worker reads it at the next directory refresh, and acts on it at the next hourly pass.
            await PostgreSqlServer.AdminScalarAsync(platform, $"UPDATE platform.tenant_quotas SET value = 5 WHERE tenant_id = '{id}'", CancellationToken);
            var refreshes = RefreshCount(logs);
            clock.Advance(TimeSpan.FromMinutes(2));
            await WaitUntilAsync(() => Task.FromResult(RefreshCount(logs) > refreshes), "The worker did not read the directory again.");
            clock.Advance(TimeSpan.FromHours(1));
            await WaitUntilAsync(async () => await DenyStepAsync(realms, id) == "DISABLED", "The worker did not switch the users quota step off.");
        }
        finally
        {
            await realms.DeleteRealmsAsync();
        }

        static int RefreshCount(LogCollector logs) => logs.Entries.Count(entry => entry.Contains("Read the tenant directory again", StringComparison.Ordinal));
    }

    private static async Task<string?> DenyStepAsync(UpstreamRealms realms, string realm)
    {
        var executions = await realms.GetJsonAsync(
            $"admin/realms/{realm}/authentication/flows/{Uri.EscapeDataString(RealmSync.FirstLoginFlowAlias)}/executions");
        return executions.EnumerateArray()
            .FirstOrDefault(execution => execution.TryGetProperty("providerId", out var provider) && provider.GetString() == RealmUsersQuota.DenyAccessProvider)
            is { ValueKind: JsonValueKind.Object } deny ? deny.GetProperty("requirement").GetString() : null;
    }

    private static WebApplication BuildWorker(string platform, TimeProvider clock, params (string Key, string Value)[] extraSettings) =>
        BuildWorker(platform, clock, new LogCollector(), extraSettings);

    private static WebApplication BuildWorker(string platform, TimeProvider clock, LogCollector logs, (string Key, string Value)[] extraSettings)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = TestSecretKey,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
        };
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddSingleton(clock);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<IWorkItemHandler, RecordingHandler>();
        var app = builder.Build();
        app.MapWorkerHealth();
        return app;
    }

    // The worker reads the directory, runs the item and measures the storage as the runtime role.
    private async Task<UsageTenant> PrepareTenantAsync(string id)
    {
        var schemaConnection = await database.CreateDatabaseAsync($"worker_usage_{id}", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(schemaConnection, CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(schemaConnection).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        var role = $"wuse_{Guid.NewGuid():N}"[..16];
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        await PostgreSqlServer.AdminScalarAsync(schemaConnection, $"CREATE ROLE {role} LOGIN PASSWORD '{password}'", CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await RuntimeRoleGrants.ApplyAsync(connection, role, CancellationToken);
            await using var command = new NpgsqlCommand(
                $"CREATE TABLE test_runs (item_id uuid, tenant_id text); GRANT SELECT, INSERT ON test_runs TO {role}",
                connection);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(schemaConnection);
        return new UsageTenant(id, builder.Host!, builder.Port, builder.Database!, role, password, schemaConnection);
    }

    private static async Task SeedPlatformAsync(string platform, params UsageTenant[] tenants)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
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
                RuntimeUser = tenant.Role,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.Password),
                SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = "https://127.0.0.1:1/realms/axis",
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = $"{tenant.Id}.example.test" });
        }

        await context.SaveChangesAsync(CancellationToken);
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }

    private static async Task GrantPlatformAsync(string platform)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }

    // About a megabyte of bytes that do not compress, so the stored size is close to it.
    private static async Task InsertFileAsync(UsageTenant tenant)
    {
        await using var connection = new NpgsqlConnection(tenant.SchemaConnectionString);
        await connection.OpenAsync(CancellationToken);
        await FileStore.InsertPendingAsync(
            connection,
            new PendingFile(Guid.CreateVersion7(), Guid.NewGuid(), Guid.NewGuid(), "receipt", "receipt.bin", "application/octet-stream", FileSize, new byte[32], "requester", DateTimeOffset.UtcNow),
            new MemoryStream(RandomNumberGenerator.GetBytes(FileSize)),
            CancellationToken);
    }

    private static async Task<Guid> EnqueueAsync(UsageTenant tenant, string kind)
    {
        await using var connection = new NpgsqlConnection(tenant.SchemaConnectionString);
        await connection.OpenAsync(CancellationToken);
        return await WorkItemQueue.EnqueueAsync(connection, null, tenant.Id, kind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
    }

    private static async Task<LatestUsage?> LatestAsync(string platform, string tenantId)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT database_bytes, file_bytes, sampled_at FROM platform.tenant_usage_latest WHERE tenant_id = @tenant",
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        return await reader.ReadAsync(CancellationToken)
            ? new LatestUsage(reader.GetInt64(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    // The tenant's day rows, of one day or of all days.
    private static async Task<List<DayUsage>> DayAsync(string platform, string tenantId, DateOnly? day)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT day, database_bytes, file_bytes, users, applications, emails_sent, connector_calls, work_items_run
            FROM platform.tenant_usage WHERE tenant_id = @tenant AND (@day::date IS NULL OR day = @day) ORDER BY day
            """,
            connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.Add(new NpgsqlParameter("day", NpgsqlTypes.NpgsqlDbType.Date) { Value = day is { } value ? value : DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<DayUsage>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new DayUsage(
                reader.GetFieldValue<DateOnly>(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                reader.GetInt64(7)));
        }

        return rows;
    }

    private static (long DatabaseBytes, long FileBytes) DayTotals(DayUsage row) => (row.DatabaseBytes, row.FileBytes);

    private sealed record UsageTenant(string Id, string DatabaseHost, int DatabasePort, string DatabaseName, string Role, string Password, string SchemaConnectionString);

    private readonly record struct LatestUsage(long DatabaseBytes, long FileBytes, DateTimeOffset SampledAt)
    {
        public (long DatabaseBytes, long FileBytes) Totals => (DatabaseBytes, FileBytes);
    }

    private sealed record DayUsage(DateOnly Day, long DatabaseBytes, long FileBytes, int? Users, int? Applications, int EmailsSent, long ConnectorCalls, long WorkItemsRun);

    /// <summary>A clock that stands still until the test moves it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero).UtcTicks;

        public DateOnly Today => DateOnly.FromDateTime(GetUtcNow().UtcDateTime);

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private sealed class RecordingHandler : IWorkItemHandler
    {
        public string Kind => RecordKind;

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("INSERT INTO test_runs (item_id, tenant_id) VALUES (@item, @tenant)", context.Connection, context.Transaction);
            command.Parameters.AddWithValue("item", context.Item.Id);
            command.Parameters.AddWithValue("tenant", context.Item.TenantId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class NothingHandler : IWorkItemHandler
    {
        public string Kind => RecordKind;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Stands in for the connector call handler: it deletes its item and reports it completed.</summary>
    private sealed class DeletingExternalHandler : IExternalWorkItemHandler
    {
        public string Kind => ProcessStarts.ConnectorCallWorkItemKind;

        public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("DELETE FROM axis.process_work_items WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", item.Id);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }
    }
}
