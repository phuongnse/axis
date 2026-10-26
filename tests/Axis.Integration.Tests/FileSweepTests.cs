using Axis.Data.Files;
using Axis.Processes.Storage;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

public sealed class FileSweepTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_sweep_purges_a_deleted_file_and_a_pending_file_over_24_hours_old_and_keeps_the_young_pending_and_the_attached_file()
    {
        var tenant = await CreateTenantDatabaseAsync("file_sweep", migrateData: true);
        var now = DateTimeOffset.UtcNow;
        var files = await SeedAsync(tenant, now);

        int purged;
        await using (var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant)))
        {
            await connection.OpenAsync(CancellationToken);
            purged = await FileSweep.PurgeAsync(connection, now, CancellationToken);
        }

        Assert.Equal(2, purged);
        Assert.Equal(new[] { files.Young, files.Attached }.Order(), await FileIdsAsync(tenant, "files"));
        Assert.Equal(new[] { files.Young, files.Attached }.Order(), await FileIdsAsync(tenant, "file_contents"));
    }

    [Fact]
    public async Task A_pending_file_exactly_24_hours_old_stays()
    {
        var tenant = await CreateTenantDatabaseAsync("file_sweep_edge", migrateData: true);
        var now = DateTimeOffset.UtcNow;
        var edge = await InsertAsync(tenant, now - FileSweep.PendingLifetime, "pending");

        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant));
        await connection.OpenAsync(CancellationToken);
        var purged = await FileSweep.PurgeAsync(connection, now, CancellationToken);

        Assert.Equal(0, purged);
        Assert.Equal([edge], await FileIdsAsync(tenant, "files"));
    }

    [Fact]
    public async Task The_worker_sweeps_with_its_own_clock()
    {
        var clock = new TestClock();
        var tenant = await CreateTenantDatabaseAsync("worker_file_sweep", migrateData: true);
        var files = await SeedAsync(tenant, clock.GetUtcNow());
        var logs = new LogCollector();
        using var host = BuildHost(clock, logs, tenant);

        // The real time is months after the clock, so a worker that used the database's time would purge the young file too.
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => (await FileIdsAsync(tenant, "files")).Count == 2, "The files were not swept.");
        await host.StopAsync(CancellationToken);

        Assert.Equal(new[] { files.Young, files.Attached }.Order(), await FileIdsAsync(tenant, "files"));
        Assert.Equal(new[] { files.Young, files.Attached }.Order(), await FileIdsAsync(tenant, "file_contents"));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_worker_logs_nothing_about_the_sweep_when_the_files_table_does_not_exist_yet()
    {
        var tenant = await CreateTenantDatabaseAsync("worker_file_sweep_none", migrateData: false);
        var logs = new LogCollector();
        using var host = BuildHost(new TestClock(), logs, tenant);

        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(() => logs.Entries.Any(entry => entry.Contains("is ready for work", StringComparison.Ordinal)), "The tenant did not become ready.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
        await host.StopAsync(CancellationToken);

        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("Sweeping the files", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(TimeProvider clock, LogCollector logs, string tenant)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:00.05",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(clock);
        return builder.Build();
    }

    // The worker is ready with the processes migrations alone, and the files tables come from the data migrations.
    private async Task<string> CreateTenantDatabaseAsync(string prefix, bool migrateData)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        if (migrateData)
        {
            await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        }

        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    // A deleted file, a pending file 25 hours old, a pending file 1 hour old and an attached file 30 hours old.
    private static async Task<(Guid Deleted, Guid Old, Guid Young, Guid Attached)> SeedAsync(string tenant, DateTimeOffset now) =>
        (await InsertAsync(tenant, now.AddHours(-1), "deleted"),
            await InsertAsync(tenant, now.AddHours(-25), "pending"),
            await InsertAsync(tenant, now.AddHours(-1), "pending"),
            await InsertAsync(tenant, now.AddHours(-30), "attached"));

    private static async Task<Guid> InsertAsync(string tenant, DateTimeOffset uploadedAt, string state)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(tenant);
        await connection.OpenAsync(CancellationToken);
        await FileStore.InsertPendingAsync(
            connection,
            new PendingFile(id, Guid.NewGuid(), Guid.NewGuid(), "receipt", "receipt.pdf", "application/pdf", 3, new byte[32], "anna", uploadedAt),
            new byte[] { 1, 2, 3 },
            CancellationToken);

        if (state != "pending")
        {
            await using var command = new NpgsqlCommand(
                """UPDATE "axis"."files" SET "state" = @state, "owner_record_id" = @owner WHERE "id" = @id""",
                connection);
            command.Parameters.AddWithValue("state", state);
            command.Parameters.AddWithValue("owner", state == "attached" ? Guid.NewGuid() : DBNull.Value);
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return id;
    }

    // The ids in the table, in .NET order. For file_contents the id is the column file_id.
    private static async Task<List<Guid>> FileIdsAsync(string tenant, string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(tenant);
        await using var command = dataSource.CreateCommand(
            $"""SELECT "{(table == "files" ? "id" : "file_id")}" FROM "axis"."{table}" """);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return [.. ids.Order()];
    }

    // Months before the real time.
    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    }
}
