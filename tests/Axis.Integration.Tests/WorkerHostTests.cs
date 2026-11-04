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
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

public sealed class WorkerHostTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string RecordKind = "test.record";
    private const string ThrowKind = "test.throw";
    private const string ChainKind = "test.chain";
    private const string BindingKind = "test.binding";
    private const string PlainSecretKind = "test.plain-secret";
    private const string SecretCallKind = "test.secret-call";
    private const string LimitKind = "test.limit";
    private const string TokenValue = "s3cr3t-4f9b-x7q2";

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
        await WaitUntilAsync(async () => await CountAsync(tenantA, "axis.process_work_items") == 0, "The work items were not all completed.");
        await hostOne.StopAsync(CancellationToken);
        await hostTwo.StopAsync(CancellationToken);

        var runs = await RunsAsync(tenantA);
        Assert.Equal(50, runs.Count);
        Assert.Equal(items.Order(), runs.Select(run => run.ItemId).Order());
        Assert.Equal(50, HandlerOf(hostOne).Calls + HandlerOf(hostTwo).Calls);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_worker_hosts_with_four_loops_each_run_every_due_item_of_two_tenants_exactly_once()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_loops_a");
        var tenantB = await CreateTenantDatabaseAsync("worker_loops_b");
        var itemsA = await EnqueueManyAsync(tenantA, "a", 120);
        var itemsB = await EnqueueManyAsync(tenantB, "b", 80);
        var settings = new Dictionary<string, string?> { ["Worker:Concurrency"] = "4" };

        var logs = new LogCollector();
        using var hostOne = BuildHost("one", logs, settings, ("a", tenantA), ("b", tenantB));
        using var hostTwo = BuildHost("two", logs, settings, ("a", tenantA), ("b", tenantB));
        await hostOne.StartAsync(CancellationToken);
        await hostTwo.StartAsync(CancellationToken);
        await WaitUntilAsync(
            async () => await CountAsync(tenantA, "axis.process_work_items") == 0 && await CountAsync(tenantB, "axis.process_work_items") == 0,
            "The work items were not all completed.");
        await hostOne.StopAsync(CancellationToken);
        await hostTwo.StopAsync(CancellationToken);

        Assert.Equal(itemsA.Order(), (await RunsAsync(tenantA)).Select(run => run.ItemId).Order());
        Assert.Equal(itemsB.Order(), (await RunsAsync(tenantB)).Select(run => run.ItemId).Order());
        Assert.Equal(200, HandlerOf(hostOne).Calls + HandlerOf(hostTwo).Calls);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tenant_with_a_small_backlog_runs_all_its_items_before_a_tenant_with_a_large_one_has_run_a_hundred()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_fair_a");
        var tenantB = await CreateTenantDatabaseAsync("worker_fair_b");
        await EnqueueManyAsync(tenantA, "a", 1000);
        await EnqueueManyAsync(tenantB, "b", 10);

        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, ("a", tenantA), ("b", tenantB));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenantB, "test_runs") == 10 && await CountAsync(tenantA, "test_runs") >= 100, "The tenants did not run enough items.");

        // The large backlog is not drained: the host stops with most of it left.
        await host.StopAsync(CancellationToken);

        var lastOfB = (DateTime)(await ScalarAsync(tenantB, "SELECT max(ran_at) FROM test_runs"))!;
        await using var dataSource = NpgsqlDataSource.Create(tenantA);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM test_runs WHERE ran_at <= @at");
        command.Parameters.AddWithValue("at", lastOfB);
        var ranBeforeEnd = (long)(await command.ExecuteScalarAsync(CancellationToken))!;
        Assert.True(ranBeforeEnd < 100, $"Tenant a ran {ranBeforeEnd} items before the last item of tenant b.");
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tenant_alone_runs_all_its_due_items_without_waiting_for_the_poll_interval()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_alone");
        var items = await EnqueueManyAsync(tenantA, "a", 200);

        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, new Dictionary<string, string?> { ["Worker:PollInterval"] = "00:01:00" }, ("a", tenantA));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenantA, "axis.process_work_items") == 0, "The work items were not all completed.");
        await host.StopAsync(CancellationToken);

        Assert.Equal(items.Order(), (await RunsAsync(tenantA)).Select(run => run.ItemId).Order());
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_chain_of_items_that_each_queue_the_next_runs_to_its_end_within_one_poll_interval()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_chain");
        await EnqueueAsync(tenantA, "a", ChainKind);

        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, new Dictionary<string, string?> { ["Worker:PollInterval"] = "00:01:00" }, ("a", tenantA));
        await host.StartAsync(CancellationToken);

        // Each step takes 50 ms and the poll runs once a minute, so a step that waits for a poll would not end the chain in time.
        await WaitUntilAsync(
            async () => await CountAsync(tenantA, "test_runs") == ChainHandler.Length && await CountAsync(tenantA, "axis.process_work_items") == 0,
            "The chain did not run to its end.");
        await host.StopAsync(CancellationToken);

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
            await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 3 && await CountAsync(tenantB, "test_runs") == 3, "The work did not run three times for each tenant.");

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
    public async Task Each_tenant_resolves_its_own_value_of_the_same_binding_inside_its_work_items()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_bindings_a");
        var tenantB = await CreateTenantDatabaseAsync("worker_bindings_b");
        var itemA = await EnqueueAsync(tenantA, "a", BindingKind);
        var itemB = await EnqueueAsync(tenantB, "b", BindingKind);
        var bindings = new Dictionary<string, string?>
        {
            ["Tenants:a:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:a:Bindings:PurchasingSystemUrl:Value"] = "https://a.example.test/api",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Value"] = "https://b.example.test/api",
        };

        var logs = new LogCollector();
        using (var host = BuildHost("worker", logs, bindings, ("a", tenantA), ("b", tenantB)))
        {
            await host.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 1 && await CountAsync(tenantB, "test_runs") == 1, "The work did not run once for each tenant.");
            await host.StopAsync(CancellationToken);
        }

        Assert.Equal([new Run(itemA, "a", "https://a.example.test/api")], await RunsAsync(tenantA));
        Assert.Equal([new Run(itemB, "b", "https://b.example.test/api")], await RunsAsync(tenantB));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Work_items_that_fail_while_resolving_or_using_a_secret_log_errors_without_any_part_of_the_value()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_secret_failures");
        await EnqueueAsync(tenantA, "a", PlainSecretKind);
        await EnqueueAsync(tenantA, "a", SecretCallKind);
        var bindings = new Dictionary<string, string?>
        {
            ["Tenants:a:Bindings:PurchasingSystemBearerToken:Kind"] = "secret",
            ["Tenants:a:Bindings:PurchasingSystemBearerToken:Value"] = TokenValue,
        };

        var logs = new LogCollector();
        using (var host = BuildHost("worker", logs, bindings, ("a", tenantA)))
        {
            await host.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await CountAsync(tenantA, "axis.process_work_items") == 0, "The work items were not all completed.");
            await host.StopAsync(CancellationToken);
        }

        var errors = logs.Entries.Where(entry => entry.StartsWith("Error", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, errors.Count);

        // A failed item names its exception type only: the message and inner exceptions can quote values.
        Assert.All(errors, entry => Assert.Contains("failed with System.InvalidOperationException", entry, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.Contains("is a secret", StringComparison.Ordinal) || entry.Contains("Call refused", StringComparison.Ordinal));
        Assert.All(logs.Entries, entry =>
        {
            Assert.DoesNotContain(TokenValue, entry, StringComparison.Ordinal);
            Assert.DoesNotContain(TokenValue[..4], entry, StringComparison.Ordinal);
            Assert.DoesNotContain(TokenValue[^4..], entry, StringComparison.Ordinal);
        });
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_handler_runs_under_the_statement_limit_and_the_next_user_of_its_connection_has_none(bool throws)
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_external_limit");
        await EnqueueAsync(tenantA, "a", LimitKind);
        var handler = new LimitReadingExternalHandler(throws);

        // The pool holds one connection, which No Reset On Close would hand on with a plain SET still in force.
        await using var connections = CreateConnections(tenantA, new TenantPoolSettings(1, 0, TimeSpan.FromSeconds(60)), ";No Reset On Close=true");
        var runner = new WorkItemRunner(
            connections.Factory,
            connections.Accessor,
            [],
            NullLogger<WorkItemRunner>.Instance,
            [handler],
            new WorkItemSettings(TimeSpan.FromSeconds(5)));

        if (throws)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunNextAsync("a", _longLease, CancellationToken));
        }
        else
        {
            Assert.True(await runner.RunNextAsync("a", _longLease, CancellationToken));
        }

        Assert.Equal("0", handler.Outside);
        Assert.Equal("5s", handler.Limit);
        connections.Accessor.Current = new TenantContext("a");
        await using var connection = await connections.Factory.OpenConnectionAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SHOW statement_timeout", connection);
        Assert.Equal("0", (string?)await command.ExecuteScalarAsync(CancellationToken));
    }

    [Fact]
    public async Task In_transaction_handler_runs_in_a_transaction_with_the_statement_limit()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_transaction_limit");
        await EnqueueAsync(tenantA, "a", LimitKind);
        var handler = new LimitReadingHandler();
        await using var connections = CreateConnections(tenantA);
        var runner = new WorkItemRunner(
            connections.Factory,
            connections.Accessor,
            [handler],
            NullLogger<WorkItemRunner>.Instance,
            settings: new WorkItemSettings(TimeSpan.FromSeconds(5)));

        Assert.True(await runner.RunNextAsync("a", _longLease, CancellationToken));

        Assert.Equal("5s", handler.Limit);
    }

    [Fact]
    public async Task Worker_started_before_the_migrations_waits_then_claims_work_once_they_are_applied()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_before_migrations", migrate: false);
        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, ("a", tenantA));
        await host.StartAsync(CancellationToken);

        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains("Information: Waiting for the database of tenant a to be migrated.")), "The worker did not wait for the migration of tenant a.");

        // Two more poll intervals, so a failing poll would have logged an error by now.
        await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));

        await MigrateAsync(tenantA);

        // Between the last migration and the grant step the worker may see its tables as read-only and log a
        // failed poll. A poll that began before the grants can also log its failure after MigrateAsync returns.
        // The worker logs that the tenant is ready only after its release check read the granted tables, so
        // every poll after that log began after the grants. Only errors after it count.
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains("Information: Tenant a is ready for work.")), "The worker did not become ready for work.");
        var loggedAfterGrants = logs.Entries.Count;
        var item = await EnqueueAsync(tenantA, "a", RecordKind);
        await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 1, "The work did not run.");
        await host.StopAsync(CancellationToken);

        Assert.Equal([new Run(item, "a", "worker")], await RunsAsync(tenantA));
        Assert.Single(logs.Entries, entry => entry == "Information: Waiting for the database of tenant a to be migrated.");
        Assert.Contains("Information: Tenant a is ready for work.", logs.Entries);
        Assert.DoesNotContain(logs.Entries.Skip(loggedAfterGrants), entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_timer_due_in_three_seconds_runs_within_a_second_of_its_due_time_with_a_five_second_poll_interval()
    {
        var tenantA = await CreateTenantDatabaseAsync("worker_timer");

        // Queued before the worker starts: under the configuration source no signal tells a running worker about a new item.
        var due = DateTimeOffset.UtcNow.AddSeconds(3);
        await EnqueueAsync(tenantA, "a", RecordKind, due);
        var settings = new Dictionary<string, string?>
        {
            ["Worker:PollInterval"] = "00:00:05",
            ["Worker:IdlePollCeiling"] = "00:00:30",
        };
        var logs = new LogCollector();
        using var host = BuildHost("worker", logs, settings, ("a", tenantA));

        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenantA, "test_runs") == 1, "The timer did not run.", TimeSpan.FromSeconds(10));
        await host.StopAsync(CancellationToken);

        var ranAt = new DateTimeOffset((DateTime)(await ScalarAsync(tenantA, "SELECT ran_at FROM test_runs"))!, TimeSpan.Zero);
        var late = ranAt - due;
        Assert.True(late < TimeSpan.FromSeconds(1), $"The timer ran {late.TotalSeconds:0.00} s after its due time.");
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(string worker, LogCollector logs, params (string TenantId, string ConnectionString)[] tenants) =>
        BuildHost(worker, logs, [], tenants);

    private static IHost BuildHost(
        string worker,
        LogCollector logs,
        Dictionary<string, string?> extraSettings,
        params (string TenantId, string ConnectionString)[] tenants)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?>(extraSettings);
        settings.TryAdd("Worker:PollInterval", "00:00:00.05");
        settings.TryAdd("Worker:IdlePollCeiling", settings["Worker:PollInterval"]);
        foreach (var (tenantId, connectionString) in tenants)
        {
            settings[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            settings[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString);
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<IWorkItemHandler>(services =>
            new RecordingHandler(worker, services.GetRequiredService<ITenantContextAccessor>()));
        builder.Services.AddSingleton<IWorkItemHandler, ChainHandler>();
        builder.Services.AddSingleton<IWorkItemHandler, BindingHandler>();
        builder.Services.AddSingleton<IWorkItemHandler, PlainSecretHandler>();
        builder.Services.AddSingleton<IWorkItemHandler, SecretCallHandler>();
        return builder.Build();
    }

    private static RecordingHandler HandlerOf(IHost host) =>
        host.Services.GetServices<IWorkItemHandler>().OfType<RecordingHandler>().Single();

    private static HandBuiltWorker CreateRunner(string name, string connectionString)
    {
        var connections = CreateConnections(connectionString);
        var handler = new RecordingHandler(name, connections.Accessor);
        var runner = new WorkItemRunner(connections.Factory, connections.Accessor, [handler], NullLogger<WorkItemRunner>.Instance);
        return new HandBuiltWorker(runner, handler, connections);
    }

    private static Connections CreateConnections(string connectionString, TenantPoolSettings? pool = null, string connectionStringSuffix = "")
    {
        var options = new TenantOptions();
        options.Tenants["a"] = new TenantSettings { ConnectionString = PostgreSqlServer.RuntimeConnectionString(connectionString) + connectionStringSuffix };
        var accessor = new TenantContextAccessor();
        return new Connections(new TenantConnectionFactory(ConfigurationTenantCatalog.FromOptions(options), accessor, pool ?? TenantPoolSettings.Default), accessor);
    }

    private async Task<string> CreateTenantDatabaseAsync(string prefix, bool migrate = true)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        if (migrate)
        {
            await MigrateAsync(connectionString);
        }

        // The test tables are in the public schema, so they do not count as migrations. The runtime role needs
        // its own grant on them.
        await using (var dataSource = NpgsqlDataSource.Create(connectionString))
        await using (var command = dataSource.CreateCommand(
            "CREATE TABLE test_runs (item_id uuid, tenant_id text, worker text, ran_at timestamptz DEFAULT clock_timestamp()); CREATE TABLE test_failures (item_id uuid, message text); "
            + $"GRANT SELECT, INSERT, UPDATE, DELETE ON test_runs, test_failures TO {PostgreSqlServer.RuntimeRole}"))
        {
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return connectionString;
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await context.Database.MigrateAsync(CancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
    }

    private static async Task<Guid> EnqueueAsync(string connectionString, string tenantId, string kind, DateTimeOffset? dueAt = null)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);

        // Due a minute ago, so a small clock difference with the database does not delay the claim.
        return await WorkItemQueue.EnqueueAsync(connection, null, tenantId, kind, dueAt ?? DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
    }

    /// <summary>Enqueues many due items over one open connection.</summary>
    private static async Task<List<Guid>> EnqueueManyAsync(string connectionString, string tenantId, int count)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        var items = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            items.Add(await WorkItemQueue.EnqueueAsync(connection, null, tenantId, RecordKind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken));
        }

        return items;
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

    /// <summary>Records a run, takes 50 ms, and queues the next item of the chain in the same transaction until the chain has its length.</summary>
    private sealed class ChainHandler : IWorkItemHandler
    {
        public const int Length = 20;

        public string Kind => ChainKind;

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using (var command = new NpgsqlCommand(
                "INSERT INTO test_runs (item_id, tenant_id, worker) VALUES (@item, @tenant, 'chain')",
                context.Connection,
                context.Transaction))
            {
                command.Parameters.AddWithValue("item", context.Item.Id);
                command.Parameters.AddWithValue("tenant", context.Item.TenantId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            long runs;
            await using (var command = new NpgsqlCommand("SELECT count(*) FROM test_runs", context.Connection, context.Transaction))
            {
                runs = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            }

            if (runs < Length)
            {
                await WorkItemQueue.EnqueueAsync(context.Connection, context.Transaction, context.Item.TenantId, ChainKind, DateTimeOffset.UtcNow.AddMinutes(-1), cancellationToken);
            }
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Records each run in <c>test_runs</c> with the current tenant's <c>PurchasingSystemUrl</c> as the worker.</summary>
    private sealed class BindingHandler(IDeploymentBindings bindings, ITenantContextAccessor accessor) : IWorkItemHandler
    {
        public string Kind => BindingKind;

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO test_runs (item_id, tenant_id, worker) VALUES (@item, @tenant, @worker)",
                context.Connection,
                context.Transaction);
            command.Parameters.AddWithValue("item", context.Item.Id);
            command.Parameters.AddWithValue("tenant", accessor.Current!.TenantId);
            command.Parameters.AddWithValue("worker", bindings.Resolve("PurchasingSystemUrl").Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Asks for the secret <c>PurchasingSystemBearerToken</c> as a plain binding, which throws.</summary>
    private sealed class PlainSecretHandler(IDeploymentBindings bindings) : IWorkItemHandler
    {
        public string Kind => PlainSecretKind;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            bindings.Resolve("PurchasingSystemBearerToken");
            return Task.CompletedTask;
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Resolves the secret <c>PurchasingSystemBearerToken</c>, then fails as a refused call that names it.</summary>
    private sealed class SecretCallHandler(IDeploymentBindings bindings) : IWorkItemHandler
    {
        public string Kind => SecretCallKind;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            var secret = bindings.ResolveSecret("PurchasingSystemBearerToken");
            throw new InvalidOperationException($"Call refused with token {secret}");
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

    /// <summary>Reads <c>statement_timeout</c> in the work transaction.</summary>
    private sealed class LimitReadingHandler : IWorkItemHandler
    {
        public string Kind => LimitKind;

        public string? Limit { get; private set; }

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SHOW statement_timeout", context.Connection, context.Transaction);
            Limit = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Reads <c>statement_timeout</c> on the bare connection, then in a transaction that begins through
    /// <see cref="WorkItemQueue.BeginAsync"/>, where it deletes the item or throws.
    /// </summary>
    private sealed class LimitReadingExternalHandler(bool throws) : IExternalWorkItemHandler
    {
        public string Kind => LimitKind;

        public string? Outside { get; private set; }

        public string? Limit { get; private set; }

        public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using (var command = new NpgsqlCommand("SHOW statement_timeout", connection))
            {
                Outside = (string?)await command.ExecuteScalarAsync(cancellationToken);
            }

            await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
            await using (var command = new NpgsqlCommand("SHOW statement_timeout", connection, transaction))
            {
                Limit = (string?)await command.ExecuteScalarAsync(cancellationToken);
            }

            if (throws)
            {
                throw new InvalidOperationException("boom");
            }

            Assert.True(await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return WorkItemOutcome.Completed;
        }
    }
}
