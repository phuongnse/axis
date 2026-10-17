using System.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Data.Schema;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A pending <c>renameField</c>, <c>fill</c> and <c>convert</c> migration each activate within the
/// time limit over a table of a hundred thousand rows, and the rows hold the expected values.
/// </summary>
public sealed class MigrationPerformanceTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const int Rows = 100_000;

    // A rename changes the catalog only, so it is expected to take milliseconds (about 20 ms when
    // measured). The bound is far above that, so shared CI runners do not make the test flaky. It
    // still catches a rename that copies or rewrites the table.
    private static readonly TimeSpan _renameTime = TimeSpan.FromSeconds(10);

    // A fill updates half the rows and then sets the column NOT NULL. A convert rewrites every row
    // and its column type. Each took under a second when measured (about 0.4 s and 0.7 s). The
    // bound is far above that, so shared CI runners do not make the test flaky. It still catches a
    // slowdown of two orders of magnitude, such as a statement per row.
    private static readonly TimeSpan _rewriteTime = TimeSpan.FromMinutes(1);

    // Each test uses its own application, entity and migration ids and name, so tests sharing the
    // database do not see each other's tables, records and active releases.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _migrationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";
    private const string NoteField = """{ "name": "note", "type": "text" }""";
    private const string RequiredNoteField = """{ "name": "note", "type": "text", "required": true }""";
    private const string RemarkField = """{ "name": "remark", "type": "text" }""";
    private const string TextQuantityField = """{ "name": "quantity", "type": "text" }""";
    private const string IntegerQuantityField = """{ "name": "quantity", "type": "integer" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pending_rename_over_a_hundred_thousand_rows_activates_within_the_time_limit_and_keeps_every_value()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertRowsAsync(("f_number", "'PO-' || n"), ("f_note", "CASE WHEN n % 2 = 0 THEN 'kept' END"));
        folder
            .With("entities/order.json", Order(NumberField, RemarkField))
            .With("migrations/0001-rename-note.json", Migration("""[{ "type": "renameField", "from": "note", "to": "remark" }]"""));
        var compiled = await CompileAsync(folder);

        var (result, elapsed) = await TimeActivationAsync(compiled);

        Assert.Empty(result.Diagnostics);
        TestContext.Current.TestOutputHelper?.WriteLine($"The rename took {elapsed}.");
        Assert.True(elapsed < _renameTime, $"The rename took {elapsed}.");
        Assert.Equal(Rows / 2, await ReadCountAsync("count(f_remark)"));
        Assert.Equal(Rows, await ReadCountAsync("count(f_number)"));
    }

    [Fact]
    public async Task A_pending_fill_over_a_hundred_thousand_rows_activates_within_the_time_limit_and_fills_the_null_rows()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertRowsAsync(("f_number", "'PO-' || n"), ("f_note", "CASE WHEN n % 2 = 0 THEN 'kept' END"));
        folder
            .With("entities/order.json", Order(NumberField, RequiredNoteField))
            .With("migrations/0001-fill-note.json", Migration("""[{ "type": "fill", "field": "note", "value": "'n/a'" }]"""));
        var compiled = await CompileAsync(folder);

        var (result, elapsed) = await TimeActivationAsync(compiled);

        Assert.Empty(result.Diagnostics);
        TestContext.Current.TestOutputHelper?.WriteLine($"The fill took {elapsed}.");
        Assert.True(elapsed < _rewriteTime, $"The fill took {elapsed}.");
        Assert.Equal(Rows / 2, await ReadCountAsync("count(*) FILTER (WHERE f_note = 'n/a')"));
        Assert.Equal(Rows / 2, await ReadCountAsync("count(*) FILTER (WHERE f_note = 'kept')"));
        var column = Assert.Single(Assert.Single(await ReadTablesAsync()).Columns, column => column.Name == "f_note");
        Assert.True(column.NotNull);
    }

    [Fact]
    public async Task A_pending_convert_over_a_hundred_thousand_rows_activates_within_the_time_limit_and_converts_every_value()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, TextQuantityField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertRowsAsync(("f_number", "'PO-' || n"), ("f_quantity", "n::text"));
        folder
            .With("entities/order.json", Order(NumberField, IntegerQuantityField))
            .With("migrations/0001-convert-quantity.json", Migration("""[{ "type": "convert", "field": "quantity", "value": "quantity" }]"""));
        var compiled = await CompileAsync(folder);

        var (result, elapsed) = await TimeActivationAsync(compiled);

        Assert.Empty(result.Diagnostics);
        TestContext.Current.TestOutputHelper?.WriteLine($"The convert took {elapsed}.");
        Assert.True(elapsed < _rewriteTime, $"The convert took {elapsed}.");
        var column = Assert.Single(Assert.Single(await ReadTablesAsync()).Columns, column => column.Name == "f_quantity");
        Assert.Equal("bigint", column.Type);
        Assert.Equal(Rows * (Rows + 1L) / 2, await ReadCountAsync("sum(f_quantity)"));
    }

    private TemporaryFolder Application() =>
        new TemporaryFolder().With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""");

    private string Order(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private string Migration(string changes) =>
        $$"""
        { "id": "{{_migrationId}}", "kind": "migration", "name": "M{{_migrationId:N}}", "formatVersion": 1, "entity": "Order", "changes": {{changes}} }
        """;

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    // One connection for the store and the data context: the active release is written in the
    // provisioning transaction when a migration is pending.
    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
    }

    private async Task<(ActivationResult Result, TimeSpan Elapsed)> TimeActivationAsync(ReleaseCompilationResult compiled)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await ActivateAsync(compiled);
        stopwatch.Stop();
        return (result, stopwatch.Elapsed);
    }

    // Inserts the rows 1 to Rows with one statement, each column set to its SQL expression over n,
    // and updates the planner's statistics, as autovacuum does in production.
    private async Task InsertRowsAsync(params (string Column, string Expression)[] values)
    {
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using (var insert = dataSource.CreateCommand(
            $"""
            INSERT INTO {table} (id, {string.Join(", ", values.Select(value => value.Column))})
            SELECT gen_random_uuid(), {string.Join(", ", values.Select(value => value.Expression))}
            FROM generate_series(1, {Rows}) AS n
            """))
        {
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var analyze = dataSource.CreateCommand($"ANALYZE {table}");
        await analyze.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<long> ReadCountAsync(string expression)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"SELECT {expression} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))}");
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task<List<CatalogTable>> ReadTablesAsync()
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var catalog = await CatalogReader.ReadAsync(connection, null, CancellationToken);
        return [.. catalog.Tables.Where(table => table.Name == EntityNaming.Table(_orderId))];
    }
}
