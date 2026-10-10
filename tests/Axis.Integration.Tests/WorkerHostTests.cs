using Axis.Core.Tenancy;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class WorkerHostTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string RecordKind = "test.record";
    private const string ThrowKind = "test.throw";

    private static readonly TimeSpan _shortLease = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _longLease = TimeSpan.FromSeconds(30);

    // Long enough for a one second lease to have expired by the database clock.
    private static readonly TimeSpan _afterShortLease = TimeSpan.FromSeconds(1.5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_worker_hosts_run_every_due_item_exactly_once()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_two_hosts");
        var items = new List<Guid>();
        for (var i = 0; i < 50; i++)
        {
            items.Add(await EnqueueAsync(tenantA, "a", RecordKind));
        }

        var logs = new LogCollector();
        using var hostOne = BuildHost("one", logs, ("a", tenantA));
        using var hostTwo = BuildHost("two", logs, ("a", tenantA));
        await hostOne.StartAsync(CancellationToken);
        await hostTwo.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenantA, "axis.process_work_items") == 0);
        await hostOne.StopAsync(CancellationToken);
        await hostTwo.StopAsync(CancellationToken);

        var runs = await RunsAsync(tenantA);
        Assert.Equal(50, runs.Count);
        Assert.Equal(items.Order(), runs.Select(run => run.ItemId).Order());
        Assert.Equal(50, HandlerOf(hostOne).Calls + HandlerOf(hostTwo).Calls);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Item_of_a_worker_that_never_commits_is_claimed_again_only_after_its_lease_expires()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_expired_lease");
        var item = await EnqueueAsync(tenantA, "a", RecordKind);
        await using var workerA = CreateRunner("A", tenantA);
        await using var workerB = CreateRunner("B", tenantA);

        var claimed = await workerA.Runner.ClaimAsync("a", _shortLease, CancellationToken);

        Assert.Equal(item, claimed?.Id);
        Assert.False(await workerB.Runner.RunNextAsync("a", _longLease, CancellationToken));

        await Task.Delay(_afterShortLease, CancellationToken);

        Assert.True(await workerB.Runner.RunNextAsync("a", _longLease, CancellationToken));
        Assert.Equal([new Run(item, "a", "B")], await RunsAsync(tenantA));
        Assert.Equal(0L, await CountAsync(tenantA, "axis.process_work_items"));
        Assert.Equal(0, workerA.Handler.Calls);
    }

    [Fact]
    public async Task Worker_whose_expired_claim_was_taken_cannot_commit_and_the_new_claimant_commits()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_fencing");
        var item = await EnqueueAsync(tenantA, "a", RecordKind);
        await using var workerA = CreateRunner("A", tenantA);
        await using var workerB = CreateRunner("B", tenantA);

        var claimedByA = await workerA.Runner.ClaimAsync("a", _shortLease, CancellationToken);
        await Task.Delay(_afterShortLease, CancellationToken);
        var claimedByB = await workerB.Runner.ClaimAsync("a", _longLease, CancellationToken);

        Assert.NotNull(claimedByA);
        Assert.NotNull(claimedByB);
        Assert.Equal(item, claimedByB.Id);
        Assert.NotEqual(claimedByA.ClaimToken, claimedByB.ClaimToken);

        Assert.Equal(WorkItemOutcome.LostClaim, await workerA.Runner.ExecuteAsync(claimedByA, CancellationToken));
        Assert.Equal(1, workerA.Handler.Calls);
        Assert.Equal(WorkItemOutcome.Completed, await workerB.Runner.ExecuteAsync(claimedByB, CancellationToken));
        Assert.Equal([new Run(item, "a", "B")], await RunsAsync(tenantA));
        Assert.Equal(0L, await CountAsync(tenantA, "axis.process_work_items"));
    }

    [Fact]
    public async Task One_worker_runs_each_item_with_its_tenant_context_against_its_own_tenant_database()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_tenant_a");
        var tenantB = await CreateTenantDatabaseAsync("worker_tenant_b");

        // Due first, so it would be claimed first if the tenant id were not checked.
        var stray = await EnqueueAsync(tenantA, "b", RecordKind, DateTimeOffset.UtcNow.AddHours(-1));
        var itemsA = new List<Guid>();
        var itemsB = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            itemsA.Add(await EnqueueAsync(tenantA, "a", RecordKind));
            itemsB.Add(await EnqueueAsync(tenantB, "b", RecordKind));
        }

        var logs = new LogCollector();
        using (var host = BuildHost("worker", logs, ("a", tenantA), ("b", tenantB)))
        {
            await host.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 3 && await CountAsync(tenantB, "test_runs") == 3);

            // Another poll of both tenants, so a claim of the stray item would have happened by now.
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
            await host.StopAsync(CancellationToken);
        }

        var runsA = await RunsAsync(tenantA);
        var runsB = await RunsAsync(tenantB);
        Assert.All(runsA, run => Assert.Equal("a", run.TenantId));
        Assert.All(runsB, run => Assert.Equal("b", run.TenantId));
        Assert.Equal(itemsA.Order(), runsA.Select(run => run.ItemId).Order());
        Assert.Equal(itemsB.Order(), runsB.Select(run => run.ItemId).Order());
        Assert.Equal(0L, await CountAsync(tenantB, "axis.process_work_items"));
        Assert.Equal(1L, await CountAsync(tenantA, "axis.process_work_items"));
        Assert.True(await ScalarAsync(tenantA, $"SELECT claim_token IS NULL FROM axis.process_work_items WHERE id = '{stray}'") is true);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Throwing_handler_rolls_back_its_writes_and_its_failure_callback_runs_once_and_the_item_is_gone()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_failure");
        var item = await EnqueueAsync(tenantA, "a", ThrowKind);
        var handler = new ThrowingHandler();
        await using var connections = CreateConnections(tenantA);
        var runner = new WorkItemRunner(connections.Factory, connections.Accessor, [handler], NullLogger<WorkItemRunner>.Instance);

        Assert.True(await runner.RunNextAsync("a", _longLease, CancellationToken));

        Assert.Empty(await RunsAsync(tenantA));
        Assert.Equal(1, handler.Failures);
        Assert.NotNull(handler.Thrown);
        Assert.Same(handler.Thrown, handler.Error);
        Assert.Equal($"{item}:boom", await ScalarAsync(tenantA, "SELECT string_agg(item_id || ':' || message, ',') FROM test_failures"));
        Assert.Equal(0L, await CountAsync(tenantA, "axis.process_work_items"));
        Assert.False(await runner.RunNextAsync("a", _longLease, CancellationToken));
    }

    [Fact]
    public async Task Worker_started_before_the_migrations_waits_then_claims_work_once_they_are_applied()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_before_migrations", migrate: false);
        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, ("a", tenantA));
        await host.StartAsync(CancellationToken);

        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains("Information: Waiting for the database of tenant a to be migrated.")));

        // Two more poll intervals, so a failing poll would have logged an error by now.
        await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));

        await MigrateAsync(tenantA);
        var item = await EnqueueAsync(tenantA, "a", RecordKind);
        await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 1);
        await host.StopAsync(CancellationToken);

        Assert.Equal([new Run(item, "a", "worker")], await RunsAsync(tenantA));
        Assert.Single(logs.Entries, entry => entry == "Information: Waiting for the database of tenant a to be migrated.");
        Assert.Contains("Information: Tenant a is ready for work.", logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(string worker, LogCollector logs, params (string TenantId, string ConnectionString)[] tenants)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?> { ["Worker:PollInterval"] = "00:00:00.05" };
        foreach (var (tenantId, connectionString) in tenants)
        {
            settings[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            settings[$"Tenants:{tenantId}:ConnectionString"] = connectionString;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration);
        builder.Services.AddSingleton<IWorkItemHandler>(services =>
            new RecordingHandler(worker, services.GetRequiredService<ITenantContextAccessor>()));
        return builder.Build();
    }

    private static RecordingHandler HandlerOf(IHost host) =>
        (RecordingHandler)host.Services.GetRequiredService<IWorkItemHandler>();

    private static HandBuiltWorker CreateRunner(string name, string connectionString)
    {
        var connections = CreateConnections(connectionString);
        var handler = new RecordingHandler(name, connections.Accessor);
        var runner = new WorkItemRunner(connections.Factory, connections.Accessor, [handler], NullLogger<WorkItemRunner>.Instance);
        return new HandBuiltWorker(runner, handler, connections);
    }

    private static Connections CreateConnections(string connectionString)
    {
        var options = new TenantOptions();
        options.Tenants["a"] = new TenantSettings { ConnectionString = connectionString };
        var accessor = new TenantContextAccessor();
        return new Connections(new TenantConnectionFactory(options, accessor), accessor);
    }

    private async Task<string> CreateTenantDatabaseAsync(string name, bool migrate = true)
    {
        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            // CREATE DATABASE cannot run in a transaction block, so each statement is its own command.
            foreach (var sql in new[] { $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", $"""CREATE DATABASE "{name}" """ })
            {
                await using var command = dataSource.CreateCommand(sql);
                await command.ExecuteNonQueryAsync(CancellationToken);
            }
        }

        var connectionString = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = name }.ConnectionString;
        if (migrate)
        {
            await MigrateAsync(connectionString);
        }

        // The test tables are in the public schema, so they do not count as migrations.
        await using (var dataSource = NpgsqlDataSource.Create(connectionString))
        await using (var command = dataSource.CreateCommand(
            "CREATE TABLE test_runs (item_id uuid, tenant_id text, worker text); CREATE TABLE test_failures (item_id uuid, message text)"))
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return connectionString;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await context.Database.MigrateAsync(CancellationToken);
    }

    private static async Task<Guid> EnqueueAsync(string connectionString, string tenantId, string kind, DateTimeOffset? dueAt = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);

        // Due a minute ago, so a small clock difference with the database does not delay the claim.
        return await WorkItemQueue.EnqueueAsync(connection, null, tenantId, kind, dueAt ?? DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
    }

    private static async Task<List<Run>> RunsAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand("SELECT item_id, tenant_id, worker FROM test_runs ORDER BY item_id");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var runs = new List<Run>();
        while (await reader.ReadAsync(CancellationToken))
        {
            runs.Add(new Run(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return runs;
    }

    private static async Task<long> CountAsync(string connectionString, string table) =>
        (long)(await ScalarAsync(connectionString, $"SELECT count(*) FROM {table}"))!;

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync(CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The workers did not finish within 30 seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
        }
    }

    private sealed record Run(Guid ItemId, string TenantId, string Worker);

    private sealed record Connections(TenantConnectionFactory Factory, TenantContextAccessor Accessor) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    /// <summary>A worker built by hand, so a test decides when it claims and when it runs.</summary>
    private sealed record HandBuiltWorker(WorkItemRunner Runner, RecordingHandler Handler, Connections Connections) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Connections.DisposeAsync();
    }

    /// <summary>Records each run in <c>test_runs</c> with the current tenant, in the work transaction.</summary>
    private sealed class RecordingHandler(string worker, ITenantContextAccessor accessor) : IWorkItemHandler
    {
        private int _calls;

        public string Kind => RecordKind;

        public int Calls => _calls;

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await using var command = new NpgsqlCommand(
                "INSERT INTO test_runs (item_id, tenant_id, worker) VALUES (@item, @tenant, @worker)",
                context.Connection,
                context.Transaction);
            command.Parameters.AddWithValue("item", context.Item.Id);
            command.Parameters.AddWithValue("tenant", accessor.Current!.TenantId);
            command.Parameters.AddWithValue("worker", worker);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Writes a run, then throws. Its failure callback records the error in <c>test_failures</c>.</summary>
    private sealed class ThrowingHandler : IWorkItemHandler
    {
        public string Kind => ThrowKind;

        public int Failures { get; private set; }

        public Exception? Thrown { get; private set; }

        public Exception? Error { get; private set; }

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using (var command = new NpgsqlCommand(
                "INSERT INTO test_runs (item_id, tenant_id, worker) VALUES (@item, @tenant, 'throwing')",
                context.Connection,
                context.Transaction))
            {
                command.Parameters.AddWithValue("item", context.Item.Id);
                command.Parameters.AddWithValue("tenant", context.Item.TenantId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            Thrown = new InvalidOperationException("boom");
            throw Thrown;
        }

        public async Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken)
        {
            Failures++;
            Error = exception;
            await using var command = new NpgsqlCommand(
                "INSERT INTO test_failures (item_id, message) VALUES (@item, @message)",
                context.Connection,
                context.Transaction);
            command.Parameters.AddWithValue("item", context.Item.Id);
            command.Parameters.AddWithValue("message", exception.Message);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
