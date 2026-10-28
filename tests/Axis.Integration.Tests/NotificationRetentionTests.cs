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

public sealed class NotificationRetentionTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_clean_up_deletes_the_notifications_over_the_retention_days_with_their_reads_and_keeps_the_newer_ones()
    {
        var tenant = await CreateTenantDatabaseAsync("notification_retention");
        var now = DateTimeOffset.UtcNow;
        var seed = await SeedAsync(tenant, now);

        int deleted;
        await using (var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant)))
        {
            await connection.OpenAsync(CancellationToken);
            deleted = await NotificationRetention.DeleteExpiredAsync(connection, now, 90, CancellationToken);
        }

        // The old notification has two recipient rows.
        Assert.Equal(2, deleted);
        Assert.Equal([seed.Young], await IdsAsync(tenant, "notifications", "id"));
        Assert.Equal([seed.Young], await IdsAsync(tenant, "notification_reads", "notification_id"));
    }

    [Fact]
    public async Task A_notification_exactly_at_the_limit_stays()
    {
        var tenant = await CreateTenantDatabaseAsync("notification_retention_edge");
        var now = DateTimeOffset.UtcNow;
        var edge = await InsertAsync(tenant, now.AddDays(-90), "requester");

        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant));
        await connection.OpenAsync(CancellationToken);
        var deleted = await NotificationRetention.DeleteExpiredAsync(connection, now, 90, CancellationToken);

        Assert.Equal(0, deleted);
        Assert.Equal([edge], await IdsAsync(tenant, "notifications", "id"));
    }

    [Fact]
    public async Task A_retention_longer_than_the_calendar_deletes_nothing()
    {
        var tenant = await CreateTenantDatabaseAsync("notification_retention_huge");
        var now = DateTimeOffset.UtcNow;
        var old = await InsertAsync(tenant, now.AddDays(-400), "requester");

        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant));
        await connection.OpenAsync(CancellationToken);
        var deleted = await NotificationRetention.DeleteExpiredAsync(connection, now, int.MaxValue, CancellationToken);

        Assert.Equal(0, deleted);
        Assert.Equal([old], await IdsAsync(tenant, "notifications", "id"));
    }

    [Fact]
    public async Task The_worker_deletes_with_its_own_clock_and_the_configured_retention_days()
    {
        var tenant = await CreateTenantDatabaseAsync("worker_notification_retention");
        var clock = new TestClock();

        // The real time is months after the clock, so a worker that used the database's time would delete the young notification too.
        await InsertAsync(tenant, clock.GetUtcNow().AddDays(-31), "requester");
        var young = await InsertAsync(tenant, clock.GetUtcNow().AddDays(-29), "requester");
        var logs = new LogCollector();
        using var host = BuildHost(clock, logs, tenant);

        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => (await IdsAsync(tenant, "notifications", "id")).Count == 1, "The old notification was not deleted.");
        await host.StopAsync(CancellationToken);

        Assert.Equal([young], await IdsAsync(tenant, "notifications", "id"));
        Assert.Equal([young], await IdsAsync(tenant, "notification_reads", "notification_id"));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(TimeProvider clock, LogCollector logs, string tenant)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:00.05",
            ["Notifications:RetentionDays"] = "30",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(clock);
        return builder.Build();
    }

    // The notification tables come from the processes migrations alone.
    private async Task<string> CreateTenantDatabaseAsync(string prefix)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    // A notification 91 days old with two recipient rows and a read, and one 89 days old with a read.
    private static async Task<(Guid Old, Guid Young)> SeedAsync(string tenant, DateTimeOffset now)
    {
        var old = await InsertAsync(tenant, now.AddDays(-91), "requester", "ben");
        var young = await InsertAsync(tenant, now.AddDays(-89), "requester");
        return (old, young);
    }

    // One row per recipient, all with the notification's id, and a read mark of the first recipient.
    private static async Task<Guid> InsertAsync(string tenant, DateTimeOffset createdAt, params string[] recipients)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(tenant);
        await connection.OpenAsync(CancellationToken);
        foreach (var recipient in recipients)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO axis.notifications (id, recipient_kind, recipient, application_id, release_id, subject_key, body_key, values, created_at)
                VALUES (@id, 'user', @recipient, @application, @release, 'subject', 'body', '{}'::jsonb, @createdAt)
                """,
                connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("recipient", recipient);
            insert.Parameters.AddWithValue("application", Guid.NewGuid());
            insert.Parameters.AddWithValue("release", Guid.NewGuid());
            insert.Parameters.AddWithValue("createdAt", createdAt);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var read = new NpgsqlCommand("INSERT INTO axis.notification_reads (notification_id, user_id) VALUES (@id, @user)", connection);
        read.Parameters.AddWithValue("id", id);
        read.Parameters.AddWithValue("user", recipients[0]);
        await read.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    // The distinct ids in the table, in .NET order.
    private static async Task<List<Guid>> IdsAsync(string tenant, string table, string column)
    {
        await using var dataSource = NpgsqlDataSource.Create(tenant);
        await using var command = dataSource.CreateCommand($"""SELECT DISTINCT "{column}" FROM "axis"."{table}" """);
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
