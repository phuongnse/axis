using System.Diagnostics;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A changed computed field recomputes at a realistic size within the time limit: activation recomputes ten
/// thousand rows inline, and the worker's batches backfill a hundred thousand rows. The query that picks each
/// batch uses the primary key.
/// </summary>
public sealed class ComputedRecomputePerformanceTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const int InlineRows = 10_000;
    private const int BackfillRows = 100_000;

    // The most claims the backfill loop makes: 200 batches of 500 rows and the batch that finds nothing, with some margin.
    private const int MaximumClaims = 250;

    // Each row costs one read and one update inside the activation transaction, so the recompute is expected to
    // take a few seconds. The bound is about ten times that, so shared CI runners do not make the test flaky,
    // and it still catches a slowdown of an order of magnitude.
    private static readonly TimeSpan _inlineRecomputeTime = TimeSpan.FromSeconds(60);

    // 200 batches of 500 rows are expected to take about a minute. Each batch rescans the rows that earlier
    // batches already stamped, because it keeps no cursor, so the cost grows faster than the row count. The
    // bound is about five times the expected time. A slowdown of an order of magnitude still fails it.
    private static readonly TimeSpan _backfillTime = TimeSpan.FromMinutes(5);

    // Each test uses its own ids, names and tenant, so tests sharing the database do not see each other's tables, records and work items.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private const string Quantity = """{ "name": "quantity", "type": "integer" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activating_a_changed_computed_field_over_ten_thousand_rows_recomputes_them_inline_within_the_time_limit()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first, ReleaseActivator.DefaultInlineRecomputeLimit);
        await InsertOrdersAsync(InlineRows, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);

        var stopwatch = Stopwatch.StartNew();
        var result = await ActivateAsync(second, ReleaseActivator.DefaultInlineRecomputeLimit);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The inline recompute of {InlineRows} rows took {stopwatch.Elapsed}.");
        Assert.Empty(result.Diagnostics);
        Assert.True(stopwatch.Elapsed < _inlineRecomputeTime, $"The inline recompute took {stopwatch.Elapsed}.");
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM axis.pending_computed_fields WHERE entity_id = @entity"));
        Assert.Equal(
            (long)InlineRows,
            await CountAsync($"""SELECT count(*) FROM {OrderTable} WHERE "computed_release_id" = @release AND "f_total" = "f_quantity" * 3""", ("release", second.Release!.Id)));
    }

    [Fact]
    public async Task The_workers_backfill_of_a_hundred_thousand_out_of_date_rows_stamps_them_all_within_the_time_limit_using_the_primary_key()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first, ReleaseActivator.DefaultInlineRecomputeLimit);
        await InsertOrdersAsync(BackfillRows, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second, ReleaseActivator.DefaultInlineRecomputeLimit);
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM axis.pending_computed_fields WHERE entity_id = @entity"));
        Assert.Equal(1L, await CountAsync("SELECT count(*) FROM axis.process_work_items WHERE entity_id = @entity AND kind = @kind", ("kind", ComputedBackfill.WorkItemKind)));
        await ExecuteAsync($"ANALYZE {OrderTable}");

        Assert.True(second.Model!.TryGetEntity("Order", out var order));
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var plan = await QueryPlans.ExplainAsync(
            dataSource,
            ComputedBackfill.OutOfDateBatchSql(order),
            command => command.Parameters.AddWithValue("current", new[] { second.Release!.Id }),
            CancellationToken);
        QueryPlans.AssertUsesIndex(plan, "pk_" + EntityNaming.Table(_orderId));

        var stopwatch = Stopwatch.StartNew();
        var outcomes = await RunBatchesAsync();
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The backfill of {BackfillRows} rows took {stopwatch.Elapsed} in {outcomes.Count} claims.");
        Assert.Equal(WorkItemOutcome.Completed, outcomes[^1]);
        Assert.True(stopwatch.Elapsed < _backfillTime, $"The backfill took {stopwatch.Elapsed}.");
        Assert.Equal(
            (long)BackfillRows,
            await CountAsync($"""SELECT count(*) FROM {OrderTable} WHERE "computed_release_id" = @release AND "f_total" = "f_quantity" * 3""", ("release", second.Release!.Id)));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM axis.pending_computed_fields WHERE entity_id = @entity"));
        Assert.Equal(0L, await CountAsync("SELECT count(*) FROM axis.process_work_items WHERE entity_id = @entity AND kind = @kind", ("kind", ComputedBackfill.WorkItemKind)));
    }

    /// <summary>Claims and runs the tenant's backfill item until it completes, or <see cref="MaximumClaims"/> claims ran. Returns the outcomes.</summary>
    private async Task<List<WorkItemOutcome>> RunBatchesAsync()
    {
        var handler = new ComputedBackfillHandler(new ReleaseModelCache(PackageCatalog.Empty), NullLogger<ComputedBackfillHandler>.Instance);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var outcomes = new List<WorkItemOutcome>();
        while (outcomes.Count < MaximumClaims)
        {
            var item = await WorkItemQueue.ClaimAsync(connection, _tenant, [ComputedBackfill.WorkItemKind], TimeSpan.FromMinutes(5), CancellationToken);
            Assert.NotNull(item);
            Assert.Equal(_orderId, item.EntityId);
            outcomes.Add(await handler.ExecuteAsync(item, connection, CancellationToken));
            if (outcomes[^1] != WorkItemOutcome.Rescheduled)
            {
                break;
            }
        }

        return outcomes;
    }

    private static string Total(string expression) =>
        $$"""{ "name": "total", "type": "integer", "expression": "{{expression}}" }""";

    private string OrderFile(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private TemporaryFolder OrderFolder(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/order.json", OrderFile(fields));

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        return compiled;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, int inlineRecomputeLimit)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(_tenant), OpenInstanceCounter.Instance, inlineRecomputeLimit, CancellationToken);
    }

    private string OrderTable => EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));

    /// <summary>Inserts <paramref name="count"/> orders in one statement, stamped with the release and holding the totals its <c>quantity * 2</c> stored.</summary>
    private Task InsertOrdersAsync(int count, Guid stamp) =>
        ExecuteAsync(
            $"""
            INSERT INTO {OrderTable} ("id", "version", "computed_release_id", "f_quantity", "f_total")
            SELECT gen_random_uuid(), 1, @stamp, n, n * 2 FROM generate_series(1, @count) AS n
            """,
            ("stamp", stamp),
            ("count", count));

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    // Counts with the order entity id as @entity.
    private async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        if (sql.Contains("@entity", StringComparison.Ordinal))
        {
            command.Parameters.AddWithValue("entity", _orderId);
        }

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
