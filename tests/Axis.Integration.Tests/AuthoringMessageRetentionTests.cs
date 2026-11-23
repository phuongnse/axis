using Axis.Configuration.Storage;
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

public sealed class AuthoringMessageRetentionTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Messages = """[{"role":"user","content":[]}]""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_clean_up_clears_the_messages_of_a_step_that_finished_over_30_days_ago_and_keeps_the_step()
    {
        var tenant = await CreateTenantDatabaseAsync("auth_msg_retention");
        var now = DateTimeOffset.UtcNow;
        var seed = await SeedAsync(tenant, now);

        int cleared;
        await using (var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant)))
        {
            await connection.OpenAsync(CancellationToken);
            cleared = await AuthoringMessageRetention.ClearExpiredAsync(connection, now, CancellationToken);
        }

        Assert.Equal(1, cleared);
        Assert.Equal(3, await StepCountAsync(tenant));
        Assert.False(await HasMessagesAsync(tenant, seed.Old));
        Assert.True(await HasMessagesAsync(tenant, seed.Young));
        Assert.True(await HasMessagesAsync(tenant, seed.Unfinished));
    }

    [Fact]
    public async Task A_step_that_finished_exactly_30_days_ago_keeps_its_messages()
    {
        var tenant = await CreateTenantDatabaseAsync("auth_msg_retention_edge");
        var now = DateTimeOffset.UtcNow;
        var request = await InsertRequestAsync(tenant);
        var edge = await InsertStepAsync(tenant, request, 1, now.AddDays(-30));

        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(tenant));
        await connection.OpenAsync(CancellationToken);
        var cleared = await AuthoringMessageRetention.ClearExpiredAsync(connection, now, CancellationToken);

        Assert.Equal(0, cleared);
        Assert.True(await HasMessagesAsync(tenant, edge));
    }

    [Fact]
    public async Task The_worker_clears_with_its_own_clock()
    {
        var tenant = await CreateTenantDatabaseAsync("worker_auth_msg_retention");
        var clock = new TestClock();

        // The real time is months after the clock, so a worker that used the database's time would clear the young step too.
        var request = await InsertRequestAsync(tenant);
        var old = await InsertStepAsync(tenant, request, 1, clock.GetUtcNow().AddDays(-31));
        var young = await InsertStepAsync(tenant, request, 2, clock.GetUtcNow().AddDays(-29));
        var logs = new LogCollector();
        using var host = BuildHost(clock, logs, tenant);

        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => !await HasMessagesAsync(tenant, old), "The old step's messages were not cleared.");
        await host.StopAsync(CancellationToken);

        Assert.True(await HasMessagesAsync(tenant, young));
        Assert.Equal(2, await StepCountAsync(tenant));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(TimeProvider clock, LogCollector logs, string tenant)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:00.05",
            ["Worker:IdlePollCeiling"] = "00:00:00.05",
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenant),
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(clock);
        return builder.Build();
    }

    // The worker calls a tenant ready after the processes migrations, and the authoring tables come from the configuration migrations.
    private async Task<string> CreateTenantDatabaseAsync(string prefix)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await using (var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    // A step that finished 31 days ago, one that finished 29 days ago and one that has not finished, all with messages.
    private static async Task<(Guid Old, Guid Young, Guid Unfinished)> SeedAsync(string tenant, DateTimeOffset now)
    {
        var request = await InsertRequestAsync(tenant);
        var old = await InsertStepAsync(tenant, request, 1, now.AddDays(-31));
        var young = await InsertStepAsync(tenant, request, 2, now.AddDays(-29));
        var unfinished = await InsertStepAsync(tenant, request, 3, null);
        return (old, young, unfinished);
    }

    private static async Task<Guid> InsertRequestAsync(string tenant)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(tenant);
        await connection.OpenAsync(CancellationToken);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO axis.authoring_requests (id, application_id, author, text, state) VALUES (@id, @application, 'author', 'Add a field', 'working')",
            connection);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("application", Guid.NewGuid());
        await insert.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<Guid> InsertStepAsync(string tenant, Guid request, int ordinal, DateTimeOffset? finishedAt)
    {
        var id = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(tenant);
        await connection.OpenAsync(CancellationToken);
        await using var insert = new NpgsqlCommand(
            $"""
            INSERT INTO axis.authoring_request_steps (id, request_id, ordinal, kind, messages, finished_at)
            VALUES (@id, @request, @ordinal, 'working', '{Messages}'::jsonb, @finishedAt)
            """,
            connection);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("request", request);
        insert.Parameters.AddWithValue("ordinal", ordinal);
        insert.Parameters.Add(new NpgsqlParameter("finishedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = (object?)finishedAt ?? DBNull.Value });
        await insert.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<bool> HasMessagesAsync(string tenant, Guid step)
    {
        await using var dataSource = NpgsqlDataSource.Create(tenant);
        await using var command = dataSource.CreateCommand("SELECT messages IS NOT NULL FROM axis.authoring_request_steps WHERE id = @id");
        command.Parameters.AddWithValue("id", step);
        return (bool)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static async Task<long> StepCountAsync(string tenant)
    {
        await using var dataSource = NpgsqlDataSource.Create(tenant);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.authoring_request_steps");
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    // Months before the real time.
    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
    }
}
