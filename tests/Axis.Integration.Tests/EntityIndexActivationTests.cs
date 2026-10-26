using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class EntityIndexActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids and names, so tests sharing the database do
    // not see each other's tables and active releases.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_reference_column_and_every_child_table_owner_column_has_an_index_without_a_declaration()
    {
        using var folder = ApplicationFolder(indexes: null);
        var compiled = await CompileAsync(folder);

        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);

        Assert.NotNull(compiled.Model);
        var expected = new List<(string Table, string Column)>();
        foreach (var entity in compiled.Model.Entities)
        {
            var table = EntityNaming.Table(entity.Id);
            expected.AddRange(entity.Fields
                .Where(field => field.Type == FieldType.Reference)
                .Select(field => (table, EntityNaming.Column(field.Name))));
            expected.AddRange(entity.Fields
                .Where(field => field.Type == FieldType.ChildCollection)
                .Select(field => (EntityNaming.Table(field.Target!.Id), EntityNaming.OwnerColumn)));
        }

        // The order references a customer and itself, and owns lines, so there are three.
        Assert.Equal(3, expected.Count);
        foreach (var (table, column) in expected)
        {
            var indexes = await ReadIndexesAsync(table);
            Assert.Contains(
                new IndexRow(EntityNaming.Index(table, [column]), [column], Unique: false),
                indexes,
                IndexRowComparer.Instance);
        }
    }

    [Fact]
    public async Task A_declared_index_is_added_replaced_and_dropped_by_activating_releases_without_a_migration()
    {
        using var folder = ApplicationFolder(indexes: null);
        var orderTable = EntityNaming.Table(_orderId);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        Assert.Equal(["f_customer", "f_parent"], await ReadIndexColumnListsAsync(orderTable));

        folder.With("entities/order.json", OrderFile("""[{ "fields": ["a", "b"] }]"""));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var added = await ReadIndexesAsync(orderTable);
        Assert.Equal(["f_a,f_b", "f_customer", "f_parent"], ColumnLists(added));
        Assert.Contains(EntityNaming.Index(orderTable, ["f_a", "f_b"]), added.Select(index => index.Name));

        folder.With("entities/order.json", OrderFile("""[{ "fields": ["a", "c"] }]"""));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var replaced = await ReadIndexesAsync(orderTable);
        Assert.Equal(["f_a,f_c", "f_customer", "f_parent"], ColumnLists(replaced));
        Assert.DoesNotContain(EntityNaming.Index(orderTable, ["f_a", "f_b"]), replaced.Select(index => index.Name));

        folder.With("entities/order.json", OrderFile(indexes: null));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        Assert.Equal(["f_customer", "f_parent"], await ReadIndexColumnListsAsync(orderTable));

        Assert.False(Directory.Exists(Path.Combine(folder.Path, "migrations")));
    }

    [Fact]
    public async Task A_declared_unique_index_on_a_table_with_rows_is_incompatible_and_keeps_the_previous_release_active()
    {
        using var folder = ApplicationFolder(indexes: null);
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        await InsertOrderAsync();
        var orderTable = EntityNaming.Table(_orderId);

        folder.With("entities/order.json", OrderFile("""[{ "fields": ["a"], "unique": true }]"""));
        var result = await ActivateAsync(await CompileAsync(folder));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", "/indexes/0/unique", _orderId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        await using (var context = database.CreateConfigurationContext())
        {
            var active = await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
            Assert.Equal(first.Release?.Id, active?.ReleaseId);
        }

        Assert.Equal(["f_customer", "f_parent"], await ReadIndexColumnListsAsync(orderTable));
    }

    [Fact]
    public async Task A_declared_unique_index_on_an_empty_table_refuses_a_second_record_with_the_same_values_at_its_first_field()
    {
        using var folder = ApplicationFolder("""[{ "fields": ["a", "b"], "unique": true }]""");
        var compiled = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);
        Assert.NotNull(compiled.Model);
        Assert.NotNull(compiled.Release);
        Assert.True(compiled.Model.TryGetEntity("Order", out var order));
        Assert.True(order.TryGetField("title", out var title));
        Assert.True(order.TryGetField("a", out var a));
        Assert.True(order.TryGetField("b", out var b));

        var first = await CreateOrderAsync(compiled, order, [new RecordValue(title, "PO"), new RecordValue(a, 1L), new RecordValue(b, 2L)]);
        var other = await CreateOrderAsync(compiled, order, [new RecordValue(title, "PO"), new RecordValue(a, 1L), new RecordValue(b, 3L)]);
        var repeated = await CreateOrderAsync(compiled, order, [new RecordValue(title, "PO"), new RecordValue(a, 1L), new RecordValue(b, 2L)]);

        Assert.Equal(RecordWriteOutcome.Written, first.Outcome);
        Assert.Equal(RecordWriteOutcome.Written, other.Outcome);
        Assert.Equal(RecordWriteOutcome.UniqueViolation, repeated.Outcome);
        Assert.NotNull(repeated.Errors);
        Assert.Equal(["/values/a"], repeated.Errors.Keys);
    }

    [Fact]
    public async Task Renaming_a_field_renames_its_unique_index_and_dropping_a_field_drops_its_index_on_a_table_with_rows()
    {
        using var folder = ApplicationFolder("""[{ "fields": ["a", "b"], "unique": true }, { "fields": ["c"] }]""");
        var orderTable = EntityNaming.Table(_orderId);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertOrderAsync();

        folder
            .With("entities/order.json", OrderFile("""[{ "fields": ["alpha", "b"], "unique": true }, { "fields": ["c"] }]""", aName: "alpha"))
            .With("migrations/0001-rename-a.json", Migration("""[{ "type": "renameField", "from": "a", "to": "alpha" }]"""));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);

        var renamed = await ReadIndexesAsync(orderTable);
        var unique = Assert.Single(renamed, index => index.Unique);
        Assert.Equal(EntityNaming.Index(orderTable, ["f_alpha", "f_b"]), unique.Name);
        Assert.Equal(["f_alpha", "f_b"], unique.Columns);

        folder
            .With("entities/order.json", OrderFile("""[{ "fields": ["alpha", "b"], "unique": true }]""", aName: "alpha", withoutC: true))
            .With("migrations/0002-drop-c.json", Migration("""[{ "type": "dropField", "field": "c" }]"""));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);

        Assert.Equal(["f_alpha,f_b", "f_customer", "f_parent"], await ReadIndexColumnListsAsync(orderTable));
    }

    [Fact]
    public async Task Activation_leaves_a_new_index_on_a_table_above_the_limit_to_the_builder()
    {
        var (compiled, orderTable) = await ActivateDeferredIndexAsync();
        var index = EntityNaming.Index(orderTable, ["f_a"]);

        Assert.DoesNotContain(index, (await ReadIndexesAsync(orderTable)).Select(row => row.Name));

        IReadOnlyList<string> built;
        await using (var connection = await OpenSchemaConnectionAsync())
        {
            built = await EntityIndexBuilder.BuildAsync(compiled.Model!, connection, CancellationToken);
        }

        Assert.Equal([index], built);
        var row = Assert.Single(await ReadIndexesAsync(orderTable), row => row.Name == index);
        Assert.True(row.Valid);
        Assert.Equal(["f_a"], row.Columns);
    }

    [Fact]
    public async Task Writes_to_the_table_continue_while_the_builder_builds_an_index()
    {
        var (compiled, orderTable) = await ActivateDeferredIndexAsync();
        var index = EntityNaming.Index(orderTable, ["f_a"]);
        var qualified = EntityNaming.QualifiedTable(orderTable);

        await using var blocker = await OpenSchemaConnectionAsync();
        await using (var begin = new NpgsqlCommand($"""BEGIN; INSERT INTO {qualified} ("id", "f_title") VALUES ('{Guid.NewGuid()}', 'blocker')""", blocker))
        {
            await begin.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var builder = await OpenSchemaConnectionAsync();
        var build = Task.Run(() => EntityIndexBuilder.BuildAsync(compiled.Model!, builder, CancellationToken), CancellationToken);
        try
        {
            await WaitForBuildAsync(qualified, build);

            await using var writer = await OpenSchemaConnectionAsync();
            await using (var timeout = new NpgsqlCommand("SET statement_timeout = '5s'", writer))
            {
                await timeout.ExecuteNonQueryAsync(CancellationToken);
            }

            await using var insert = new NpgsqlCommand($"""INSERT INTO {qualified} ("id", "f_title") VALUES (@id, 'during')""", writer);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(CancellationToken));
            Assert.False(build.IsCompleted);
        }
        finally
        {
            await using var commit = new NpgsqlCommand("COMMIT", blocker);
            await commit.ExecuteNonQueryAsync(CancellationToken);
        }

        Assert.Equal([index], await build);
        Assert.True(Assert.Single(await ReadIndexesAsync(orderTable), row => row.Name == index).Valid);
    }

    [Fact]
    public async Task The_builder_drops_and_builds_again_an_index_that_a_cancelled_build_left_invalid()
    {
        var (compiled, orderTable) = await ActivateDeferredIndexAsync();
        var index = EntityNaming.Index(orderTable, ["f_a"]);
        var qualified = EntityNaming.QualifiedTable(orderTable);

        await using (var blocker = await OpenSchemaConnectionAsync())
        {
            await using (var begin = new NpgsqlCommand($"""BEGIN; INSERT INTO {qualified} ("id", "f_title") VALUES ('{Guid.NewGuid()}', 'blocker')""", blocker))
            {
                await begin.ExecuteNonQueryAsync(CancellationToken);
            }

            await using var cancelled = await OpenSchemaConnectionAsync();
            await using (var timeout = new NpgsqlCommand("SET statement_timeout = '1s'", cancelled))
            {
                await timeout.ExecuteNonQueryAsync(CancellationToken);
            }

            await using var create = new NpgsqlCommand($"""CREATE INDEX CONCURRENTLY {EntityNaming.Quote(index)} ON {qualified} ("f_a")""", cancelled);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => create.ExecuteNonQueryAsync(CancellationToken));
            Assert.Equal("57014", exception.SqlState);

            await using var rollback = new NpgsqlCommand("ROLLBACK", blocker);
            await rollback.ExecuteNonQueryAsync(CancellationToken);
        }

        Assert.False(Assert.Single(await ReadIndexesAsync(orderTable), row => row.Name == index).Valid);

        IReadOnlyList<string> built;
        await using (var connection = await OpenSchemaConnectionAsync())
        {
            built = await EntityIndexBuilder.BuildAsync(compiled.Model!, connection, CancellationToken);
        }

        Assert.Equal([index], built);
        Assert.True(Assert.Single(await ReadIndexesAsync(orderTable), row => row.Name == index).Valid);
    }

    // Activates the application without indexes and with 25 orders, then a release that adds an index over a with the
    // limit at 10. The index is left to the builder.
    private async Task<(ReleaseCompilationResult Compiled, string OrderTable)> ActivateDeferredIndexAsync()
    {
        var folder = ApplicationFolder(indexes: null);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertOrdersAsync(25);

        folder.With("entities/order.json", OrderFile("""[{ "fields": ["a"] }]"""));
        var compiled = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(compiled, limit: 10)).Diagnostics);
        folder.Dispose();

        await using var context = database.CreateConfigurationContext();
        var active = await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
        Assert.Equal(compiled.Release?.Id, active?.ReleaseId);
        return (compiled, EntityNaming.Table(_orderId));
    }

    // Waits until the build runs on the table, which needs the blocker's open transaction to end before it goes on.
    private async Task WaitForBuildAsync(string qualifiedTable, Task build)
    {
        await using var monitor = await OpenSchemaConnectionAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM pg_stat_progress_create_index WHERE relid = '{qualifiedTable}'::regclass", monitor);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 0)
        {
            Assert.False(build.IsCompleted, "The build ended before it was held.");
            Assert.True(DateTime.UtcNow < deadline, "The build did not start.");
            await Task.Delay(50, CancellationToken);
        }
    }

    private async Task<NpgsqlConnection> OpenSchemaConnectionAsync()
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private static string Migration(string changes) =>
        $$"""
        { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "M{{Guid.NewGuid():N}}", "formatVersion": 1, "entity": "Order", "changes": {{changes}} }
        """;

    private async Task<RecordWriteResult> CreateOrderAsync(ReleaseCompilationResult compiled, EntityModel order, IReadOnlyList<RecordValue> values)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        return await RecordCommands.CreateAsync(connection, compiled.Model!, order, compiled.Release!.Id, values, [], cancellationToken: CancellationToken);
    }

    private string OrderFile(string? indexes, string aName = "a", bool withoutC = false) => OrderFile(
        indexes,
        aName,
        withoutC ? "" : "{ \"name\": \"c\", \"type\": \"integer\" },");

    private string OrderFile(string? indexes, string aName, string cField) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1,
          "displayField": "title",
          "fields": [
            { "name": "title", "type": "text", "required": true },
            { "name": "{{aName}}", "type": "integer" },
            { "name": "b", "type": "integer" },
            {{cField}}
            { "name": "customer", "type": "reference", "target": "Customer" },
            { "name": "parent", "type": "reference", "target": "Order" },
            { "name": "lines", "type": "child-collection", "target": "OrderLine" }
          ]{{(indexes is null ? "" : $", \"indexes\": {indexes}")}} }
        """;

    private TemporaryFolder ApplicationFolder(string? indexes) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/customer.json", $$"""
                { "id": "{{_customerId}}", "kind": "entity", "name": "Customer", "formatVersion": 1,
                  "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
                """)
            .With("entities/order-line.json", $$"""
                { "id": "{{_lineId}}", "kind": "entity", "name": "OrderLine", "formatVersion": 1,
                  "fields": [ { "name": "description", "type": "text" } ] }
                """)
            .With("entities/order.json", OrderFile(indexes));

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, int limit = ReleaseActivator.DefaultInlineRecomputeLimit)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, limit, CancellationToken);
    }

    private async Task InsertOrderAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))} ("id", "f_title") VALUES (@id, 'PO-1')""");
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task InsertOrdersAsync(int count)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))} ("id", "f_title") SELECT gen_random_uuid(), 'PO-' || n FROM generate_series(1, @count) n""");
        command.Parameters.AddWithValue("count", count);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<List<string>> ReadIndexColumnListsAsync(string table) => ColumnLists(await ReadIndexesAsync(table));

    private static List<string> ColumnLists(IEnumerable<IndexRow> indexes) =>
        [.. indexes.Select(index => string.Join(',', index.Columns)).Order(StringComparer.Ordinal)];

    /// <summary>The <c>ix_</c> indexes of a table, read from <c>pg_index</c>, with their columns in index order.</summary>
    private async Task<List<IndexRow>> ReadIndexesAsync(string table)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            """
            SELECT i.relname, x.indisunique,
              ARRAY(SELECT a.attname::text FROM unnest(x.indkey::int2[]) WITH ORDINALITY k(attnum, ord)
                    JOIN pg_attribute a ON a.attrelid = x.indrelid AND a.attnum = k.attnum ORDER BY k.ord),
              x.indisvalid
            FROM pg_index x
            JOIN pg_class i ON i.oid = x.indexrelid
            JOIN pg_class t ON t.oid = x.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'entities' AND t.relname = @table AND i.relname LIKE 'ix\_%'
            """);
        command.Parameters.AddWithValue("table", table);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var indexes = new List<IndexRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            indexes.Add(new IndexRow(reader.GetString(0), reader.GetFieldValue<string[]>(2), reader.GetBoolean(1)) { Valid = reader.GetBoolean(3) });
        }

        return indexes;
    }

    private sealed record IndexRow(string Name, IReadOnlyList<string> Columns, bool Unique)
    {
        public bool Valid { get; init; } = true;
    }

    private sealed class IndexRowComparer : IEqualityComparer<IndexRow>
    {
        public static IndexRowComparer Instance { get; } = new();

        public bool Equals(IndexRow? x, IndexRow? y) =>
            x is not null && y is not null && x.Name == y.Name && x.Unique == y.Unique && x.Columns.SequenceEqual(y.Columns);

        public int GetHashCode(IndexRow obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
