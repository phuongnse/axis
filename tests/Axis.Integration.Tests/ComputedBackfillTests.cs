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
/// Activation leaves a recompute of more rows than the inline limit to the worker. It marks the entity pending
/// and queues a work item, and the worker recomputes the out-of-date rows in batches without overwriting a
/// user's concurrent write.
/// </summary>
public sealed class ComputedBackfillTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const int Limit = 10;

    // Each test uses its own ids, names and tenant, so tests sharing the database do not see each other's tables, records and work items.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _migrationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private const string Quantity = """{ "name": "quantity", "type": "integer" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_changed_expression_on_more_rows_than_the_limit_marks_the_entity_pending_and_changes_no_row()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(second.Release!.Id, (await FindActiveAsync())?.ReleaseId);
        var pending = Assert.Single(await ReadPendingAsync());
        Assert.Equal(second.Release.Id, pending.TargetReleaseId);
        Assert.Equal([second.Release.Id], pending.CurrentReleaseIds);
        Assert.Equal(["total"], pending.Fields);
        Assert.Equal(1L, await CountWorkItemsAsync());
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)first.Release.Id, (long?)(quantity * 2)), (order.Version, order.Stamp, order.Total));
        }
    }

    [Fact]
    public async Task The_backfill_item_stores_the_trace_and_span_of_the_activation_that_queued_it()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        await InsertOrdersAsync(25, first.Release!.Id);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        using var activity = new Activity("test").Start();
        await ActivateAsync(second);

        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT trace_id, span_id FROM axis.process_work_items WHERE entity_id = @entity AND kind = @kind");
        command.Parameters.AddWithValue("entity", _orderId);
        command.Parameters.AddWithValue("kind", ComputedBackfill.WorkItemKind);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        Assert.Equal((activity.TraceId.ToHexString(), activity.SpanId.ToHexString()), (reader.GetString(0), reader.GetString(1)));
    }

    [Fact]
    public async Task The_workers_batches_store_the_new_values_and_stamp_on_every_row_and_remove_the_pending_row_and_item()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);

        var outcomes = await RunBatchesAsync();

        Assert.Equal([WorkItemOutcome.Rescheduled, WorkItemOutcome.Completed], outcomes);
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)second.Release!.Id, (long?)(quantity * 3)), (order.Version, order.Stamp, order.Total));
        }

        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
        Assert.Equal(0L, await CountErrorsAsync());
    }

    [Fact]
    public async Task A_row_a_user_updates_between_a_batchs_read_and_its_update_keeps_the_users_values_until_a_later_batch_takes_it()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);
        var touched = rows[12];

        WorkItemOutcome firstBatch;
        await using (var user = new NpgsqlConnection(database.ConnectionString))
        {
            await user.OpenAsync(CancellationToken);
            await using var userTransaction = await user.BeginTransactionAsync(CancellationToken);
            await using (var update = new NpgsqlCommand($"""UPDATE {OrderTable} SET "f_quantity" = 100, "version" = "version" + 1 WHERE "id" = @id""", user, userTransaction))
            {
                update.Parameters.AddWithValue("id", touched.Id);
                await update.ExecuteNonQueryAsync(CancellationToken);
            }

            var batch = Task.Run(async () => (await RunBatchesAsync(maximum: 1))[0], CancellationToken);
            await WaitForBlockedUpdateAsync();
            await userTransaction.CommitAsync(CancellationToken);
            firstBatch = await batch;
        }

        Assert.Equal(WorkItemOutcome.Rescheduled, firstBatch);
        var kept = await ReadOrderAsync(touched.Id);
        Assert.Equal((100L, 2L, (Guid?)first.Release.Id, (long?)(touched.Quantity * 2)), (kept.Quantity, kept.Version, kept.Stamp, kept.Total));
        foreach (var (id, quantity) in rows.Where(row => row.Id != touched.Id))
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)second.Release!.Id, (long?)(quantity * 3)), (order.Version, order.Stamp, order.Total));
        }

        Assert.Single(await ReadPendingAsync());
        Assert.Equal(1L, await CountWorkItemsAsync());

        var later = await RunBatchesAsync();

        Assert.Equal([WorkItemOutcome.Rescheduled, WorkItemOutcome.Completed], later);
        var recomputed = await ReadOrderAsync(touched.Id);
        Assert.Equal((100L, 2L, (Guid?)second.Release!.Id, (long?)300), (recomputed.Quantity, recomputed.Version, recomputed.Stamp, recomputed.Total));
        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    [Fact]
    public async Task A_changed_expression_on_as_many_rows_as_the_limit_recomputes_inline_and_leaves_nothing_pending()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(Limit, first.Release!.Id);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)second.Release!.Id, (long?)(quantity * 3)), (order.Version, order.Stamp, order.Total));
        }

        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    [Fact]
    public async Task A_fill_on_more_rows_than_the_limit_is_left_to_the_worker_which_recomputes_and_stamps_the_filled_rows()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var filled = new List<Guid>();
        for (var index = 0; index < 25; index++)
        {
            filled.Add(await InsertOrderAsync(version: 1, stamp: first.Release!.Id, quantity: null));
        }

        var other = await InsertOrderAsync(version: 4, stamp: first.Release!.Id, quantity: 5, total: 10);

        folder.With("migrations/0001-fill-quantity.json", Migration("Order", """[{ "type": "fill", "field": "quantity", "value": "3" }]"""));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Single(await ReadPendingAsync());
        Assert.Equal(1L, await CountWorkItemsAsync());
        foreach (var id in filled)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((3L, 2L, (Guid?)null, (long?)null), (order.Quantity, order.Version, order.Stamp, order.Total));
        }

        var outcomes = await RunBatchesAsync();

        Assert.Equal(WorkItemOutcome.Completed, outcomes[^1]);
        foreach (var id in filled)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((2L, (Guid?)second.Release!.Id, (long?)6), (order.Version, order.Stamp, order.Total));
        }

        var untouched = await ReadOrderAsync(other);
        Assert.Equal((4L, (Guid?)first.Release.Id, (long?)10), (untouched.Version, untouched.Stamp, untouched.Total));
        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    [Fact]
    public async Task Activating_a_third_release_while_pending_targets_it_merges_the_fields_and_the_backfill_ends_with_its_values()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);
        Assert.Equal([WorkItemOutcome.Rescheduled], await RunBatchesAsync(maximum: 1));
        await ExecuteAsync("UPDATE axis.pending_computed_fields SET fields = fields || ARRAY['legacy'] WHERE entity_id = @entity");

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 4")));
        var third = await CompileAsync(folder);
        var result = await ActivateAsync(third);

        Assert.Empty(result.Diagnostics);
        var pending = Assert.Single(await ReadPendingAsync());
        Assert.Equal(third.Release!.Id, pending.TargetReleaseId);
        Assert.Equal([third.Release.Id], pending.CurrentReleaseIds);
        Assert.Equal(["legacy", "total"], pending.Fields);
        Assert.Equal(1L, await CountWorkItemsAsync());

        var outcomes = await RunBatchesAsync();

        Assert.Equal(WorkItemOutcome.Completed, outcomes[^1]);
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)third.Release.Id, (long?)(quantity * 4)), (order.Version, order.Stamp, order.Total));
        }

        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    [Fact]
    public async Task Activating_a_third_release_while_pending_recomputes_inline_when_few_rows_are_out_of_date()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);
        await ExecuteAsync(
            $"""UPDATE {OrderTable} SET "computed_release_id" = @release WHERE "id" IN (SELECT "id" FROM {OrderTable} ORDER BY "id" OFFSET 5)""",
            parameters => parameters.AddWithValue("release", second.Release!.Id));
        Assert.Single(await ReadPendingAsync());

        // The third release changes no computed definition, so the rows the second release stamped are up to date.
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3"), """{ "name": "note", "type": "text" }"""));
        var third = await CompileAsync(folder);
        var result = await ActivateAsync(third);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
        var recomputed = 0;
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            if (order.Stamp == third.Release!.Id)
            {
                recomputed++;
                Assert.Equal((1L, (long?)(quantity * 3)), (order.Version, order.Total));
            }
            else
            {
                Assert.Equal((1L, (Guid?)second.Release!.Id, (long?)(quantity * 2)), (order.Version, order.Stamp, order.Total));
            }
        }

        Assert.Equal(5, recomputed);
    }

    [Fact]
    public async Task A_write_that_overlaps_the_last_batch_either_keeps_the_backfill_going_or_inserts_a_new_pending_row()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);
        Assert.Equal([WorkItemOutcome.Rescheduled], await RunBatchesAsync(maximum: 1));
        var model = first.Model!;
        var owner = model.FindEntity(_orderId)!;
        var touched = rows[7];

        // An older-release write holds the pending row while the last batch, which finds nothing, wants to end the backfill.
        WorkItemOutcome lastBatch;
        await using (var writer = new NpgsqlConnection(database.ConnectionString))
        {
            await writer.OpenAsync(CancellationToken);
            await using var writerTransaction = await writer.BeginTransactionAsync(CancellationToken);
            await using (var update = new NpgsqlCommand(
                $"""UPDATE {OrderTable} SET "computed_release_id" = @release WHERE "id" = @id""", writer, writerTransaction))
            {
                update.Parameters.AddWithValue("release", first.Release.Id);
                update.Parameters.AddWithValue("id", touched.Id);
                await update.ExecuteNonQueryAsync(CancellationToken);
            }

            await ComputedBackfill.MarkStaleWriteAsync(writer, writerTransaction, _tenant, model, owner, first.Release.Id, CancellationToken);

            var batch = Task.Run(async () => (await RunBatchesAsync(maximum: 1))[0], CancellationToken);
            await WaitForLockWaitAsync("pending_computed_fields");
            await writerTransaction.CommitAsync(CancellationToken);
            lastBatch = await batch;
        }

        Assert.Equal(WorkItemOutcome.Rescheduled, lastBatch);
        Assert.Single(await ReadPendingAsync());
        Assert.Equal(1L, await CountWorkItemsAsync());

        var later = await RunBatchesAsync();

        Assert.Equal(WorkItemOutcome.Completed, later[^1]);
        var restamped = await ReadOrderAsync(touched.Id);
        Assert.Equal((Guid?)second.Release!.Id, restamped.Stamp);
        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());

        // After the final delete, a write of the older release finds no pending row and inserts a new one.
        using var activity = new Activity("test").Start();
        await using (var writer = new NpgsqlConnection(database.ConnectionString))
        {
            await writer.OpenAsync(CancellationToken);
            await using var writerTransaction = await writer.BeginTransactionAsync(CancellationToken);
            await ComputedBackfill.MarkStaleWriteAsync(writer, writerTransaction, _tenant, model, owner, first.Release.Id, CancellationToken);
            await writerTransaction.CommitAsync(CancellationToken);
        }

        var pending = Assert.Single(await ReadPendingAsync());
        Assert.Equal(second.Release.Id, pending.TargetReleaseId);
        Assert.Equal([second.Release.Id], pending.CurrentReleaseIds);
        Assert.Equal(["total"], pending.Fields);
        Assert.Equal((activity.TraceId.ToHexString(), activity.SpanId.ToHexString()), await ReadWorkItemTraceAsync());
    }

    [Fact]
    public async Task A_write_of_a_release_with_the_active_releases_fingerprint_or_the_active_release_marks_nothing_pending()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 2"), """{ "name": "note", "type": "text" }"""));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var owner = second.Model!.FindEntity(_orderId)!;
        await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, _tenant, second.Model, owner, first.Release!.Id, CancellationToken);
        await ComputedBackfill.MarkStaleWriteAsync(connection, transaction, _tenant, second.Model, owner, second.Release!.Id, CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    [Fact]
    public async Task A_backfill_whose_pending_row_targets_an_older_release_than_the_active_one_computes_with_the_active_release()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        await ActivateAsync(second);
        var rows = await InsertOrdersAsync(25, first.Release!.Id);

        // A write inserted this pending row while the second release was still committing, so it names the first.
        await ExecuteAsync(
            """
            INSERT INTO axis.pending_computed_fields (entity_id, application_id, target_release_id, current_release_ids, fields, created_at)
            VALUES (@entity, @application, @first, @current, ARRAY['total'], now());
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, entity_id)
            VALUES (@item, @tenant, @kind, now(), @entity)
            """,
            parameters =>
            {
                parameters.AddWithValue("application", _applicationId);
                parameters.AddWithValue("first", first.Release.Id);
                parameters.AddWithValue("current", new[] { first.Release.Id });
                parameters.AddWithValue("item", Guid.NewGuid());
                parameters.AddWithValue("tenant", _tenant);
                parameters.AddWithValue("kind", ComputedBackfill.WorkItemKind);
            });

        var outcomes = await RunBatchesAsync();

        Assert.Equal(WorkItemOutcome.Completed, outcomes[^1]);
        foreach (var (id, quantity) in rows)
        {
            var order = await ReadOrderAsync(id);
            Assert.Equal((1L, (Guid?)second.Release!.Id, (long?)(quantity * 3)), (order.Version, order.Stamp, order.Total));
        }

        Assert.Empty(await ReadPendingAsync());
        Assert.Equal(0L, await CountWorkItemsAsync());
    }

    /// <summary>Claims and runs the tenant's backfill item until it completes, or <paramref name="maximum"/> batches ran. Returns the outcomes.</summary>
    private async Task<List<WorkItemOutcome>> RunBatchesAsync(int maximum = 10)
    {
        var handler = new ComputedBackfillHandler(new ReleaseModelCache(PackageCatalog.Empty), NullLogger<ComputedBackfillHandler>.Instance);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var outcomes = new List<WorkItemOutcome>();
        while (outcomes.Count < maximum)
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

    // The batch is blocked when a backend of this database waits on a lock in an update of the order table.
    private Task WaitForBlockedUpdateAsync() => WaitForLockWaitAsync(EntityNaming.Table(_orderId));

    // Waits until a backend of this database waits on a lock in a statement that mentions the text.
    private async Task WaitForLockWaitAsync(string statementText)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = dataSource.CreateCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%' || @text || '%'");
            command.Parameters.AddWithValue("text", statementText);
            if ((long)(await command.ExecuteScalarAsync(CancellationToken))! > 0)
            {
                return;
            }

            await Task.Delay(50, CancellationToken);
        }

        Assert.Fail($"No backend ever waited on a lock in a statement that mentions '{statementText}'.");
    }

    // Runs the statement with the order entity id as @entity.
    private async Task ExecuteAsync(string sql, Action<NpgsqlParameterCollection>? parameters = null)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("entity", _orderId);
        parameters?.Invoke(command.Parameters);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<(string? TraceId, string? SpanId)> ReadWorkItemTraceAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT trace_id, span_id FROM axis.process_work_items WHERE entity_id = @entity AND kind = @kind");
        command.Parameters.AddWithValue("entity", _orderId);
        command.Parameters.AddWithValue("kind", ComputedBackfill.WorkItemKind);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetString(1));
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

    private string Migration(string entity, string changes) =>
        $$"""
        { "id": "{{_migrationId}}", "kind": "migration", "name": "M{{_migrationId:N}}", "formatVersion": 1, "entity": "{{entity}}", "changes": {{changes}} }
        """;

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        return compiled;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(_tenant), OpenInstanceCounter.Instance, Limit, CancellationToken);
    }

    private async Task<ActiveRelease?> FindActiveAsync()
    {
        await using var context = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
    }

    private string OrderTable => EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));

    /// <summary>Inserts <paramref name="count"/> orders stamped with the release and holding the totals its <c>quantity * 2</c> stored.</summary>
    private async Task<List<(Guid Id, long Quantity)>> InsertOrdersAsync(int count, Guid stamp)
    {
        var rows = new List<(Guid, long)>();
        for (var index = 1; index <= count; index++)
        {
            rows.Add((await InsertOrderAsync(version: 1, stamp, quantity: index, total: index * 2), index));
        }

        return rows;
    }

    private async Task<Guid> InsertOrderAsync(long version, Guid? stamp, long? quantity, long? total = null)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"""INSERT INTO {OrderTable} ("id", "version", "computed_release_id", "f_quantity", "f_total") VALUES (@id, @version, @stamp, @quantity, @total)""");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)stamp ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("quantity", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)quantity ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("total", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)total ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task<(long? Quantity, long Version, Guid? Stamp, long? Total)> ReadOrderAsync(Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""SELECT "f_quantity", "version", "computed_release_id", "f_total" FROM {OrderTable} WHERE "id" = @id""");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3));
    }

    private async Task<List<(Guid TargetReleaseId, Guid[] CurrentReleaseIds, string[] Fields)>> ReadPendingAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT target_release_id, current_release_ids, fields FROM axis.pending_computed_fields WHERE entity_id = @entity");
        command.Parameters.AddWithValue("entity", _orderId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(Guid, Guid[], string[])>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetFieldValue<Guid[]>(1), reader.GetFieldValue<string[]>(2)));
        }

        return rows;
    }

    private async Task<long> CountWorkItemsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.process_work_items WHERE entity_id = @entity AND kind = @kind");
        command.Parameters.AddWithValue("entity", _orderId);
        command.Parameters.AddWithValue("kind", ComputedBackfill.WorkItemKind);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private async Task<long> CountErrorsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.computed_field_errors WHERE entity_id = @entity");
        command.Parameters.AddWithValue("entity", _orderId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
