using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Activation recomputes the stored computed values of an entity whose computed definitions changed, and an
/// update that leaves out child rows with computed fields does not claim to have recomputed them.
/// </summary>
public sealed class ComputedRecomputeActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own ids and names, so tests sharing the database do not see each other's tables and records.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private const string Quantity = """{ "name": "quantity", "type": "integer" }""";
    private const string Note = """{ "name": "note", "type": "text" }""";
    private const string Lines = """{ "name": "lines", "type": "child-collection", "target": "Line" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activating_a_release_that_changes_an_expression_recomputes_every_row_and_stamps_it_without_changing_the_version()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var one = await InsertOrderAsync(version: 3, stamp: first.Release!.Id, quantity: 5, total: 10);
        var two = await InsertOrderAsync(version: 1, stamp: null, quantity: 7, total: 999);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 3")));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal((3L, second.Release!.Id, 15L), await ReadOrderStampAsync(one));
        Assert.Equal((1L, second.Release.Id, 21L), await ReadOrderStampAsync(two));
        Assert.Equal(0L, await CountErrorsAsync());
    }

    [Fact]
    public async Task Activating_a_release_that_adds_a_computed_field_fills_it_on_every_row()
    {
        using var folder = OrderFolder(Quantity);
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var one = await InsertOrderAsync(version: 1, stamp: first.Release!.Id, quantity: 4);
        var two = await InsertOrderAsync(version: 2, stamp: first.Release.Id, quantity: 6);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 2")));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal((1L, second.Release!.Id, 8L), await ReadOrderStampAsync(one));
        Assert.Equal((2L, second.Release.Id, 12L), await ReadOrderStampAsync(two));
    }

    [Fact]
    public async Task A_row_whose_new_expression_overflows_gets_null_and_an_error_row_and_activation_commits()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var overflowing = await InsertOrderAsync(version: 1, stamp: first.Release!.Id, quantity: 100000, total: 200000);
        var fine = await InsertOrderAsync(version: 1, stamp: first.Release.Id, quantity: 5, total: 10);

        folder.With("entities/order.json", OrderFile(Quantity, Total("quantity * 1000000000000000")));
        var second = await CompileAsync(folder);
        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(second.Release!.Id, (await FindActiveAsync())?.ReleaseId);
        Assert.Equal((1L, second.Release.Id, (long?)null), await ReadOrderStampAsync(overflowing));
        Assert.Equal((1L, second.Release.Id, 5_000_000_000_000_000L), await ReadOrderStampAsync(fine));
        var error = Assert.Single(await ReadErrorsAsync());
        Assert.Equal((_orderId, overflowing, "total", second.Release.Id, "Could not be computed."), error);
    }

    [Fact]
    public async Task Activating_a_release_with_the_same_computed_definitions_touches_no_row()
    {
        using var folder = OrderFolder(Quantity, Total("quantity * 2"));
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var other = Guid.NewGuid();
        var one = await InsertOrderAsync(version: 7, stamp: other, quantity: 5, total: 999);
        var two = await InsertOrderAsync(version: 1, stamp: null, quantity: 6, total: null);

        folder.With("entities/order.json", OrderFile(Quantity, Note, Total("quantity * 2")));
        var second = await CompileAsync(folder);
        Assert.NotEqual(first.Release!.Id, second.Release!.Id);
        Assert.Empty((await ActivateAsync(second)).Diagnostics);
        Assert.Empty((await ActivateAsync(second)).Diagnostics);

        Assert.Equal((7L, (Guid?)other, (long?)999), await ReadOrderStampAsync(one));
        Assert.Equal((1L, (Guid?)null, (long?)null), await ReadOrderStampAsync(two));
    }

    [Fact]
    public async Task An_update_of_a_later_release_without_the_lines_clears_the_stamp_and_activation_then_fixes_amounts_and_total()
    {
        using var folder = LinesFolder("quantity * 2");
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var order = await InsertOrderWithLinesAsync(first.Release!.Id, version: 1, quantities: [1, 2], factor: 2);

        folder.With("entities/line.json", LineFile("quantity * 3"));
        var second = await CompileAsync(folder);
        Assert.NotNull(second.Release);
        var update = await UpdateNoteAsync(second, second.Release.Id, order, version: 1, rows: false);

        Assert.Equal(RecordWriteOutcome.Written, update.Outcome);
        Assert.Equal(2L, update.Record!.Version);
        Assert.Equal((2L, (Guid?)null, (long?)6), await ReadOrderStampAsync(order));

        Assert.Empty((await ActivateAsync(second)).Diagnostics);

        Assert.Equal((2L, (Guid?)second.Release.Id, (long?)9), await ReadOrderStampAsync(order));
        Assert.Equal([3L, 6L], await ReadAmountsAsync(order));
    }

    [Fact]
    public async Task An_update_of_an_older_release_without_the_lines_clears_a_newer_stamp()
    {
        using var folder = LinesFolder("quantity * 2");
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var newer = Guid.CreateVersion7();
        var order = await InsertOrderWithLinesAsync(newer, version: 1, quantities: [1, 2], factor: 3);

        var update = await UpdateNoteAsync(first, first.Release!.Id, order, version: 1, rows: false);

        Assert.Equal(RecordWriteOutcome.Written, update.Outcome);
        Assert.Equal((2L, (Guid?)null, (long?)9), await ReadOrderStampAsync(order));
    }

    [Fact]
    public async Task An_update_without_the_lines_keeps_a_stamp_that_names_its_release()
    {
        using var folder = LinesFolder("quantity * 2");
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var order = await InsertOrderWithLinesAsync(first.Release!.Id, version: 1, quantities: [1, 2], factor: 2);

        var update = await UpdateNoteAsync(first, first.Release.Id, order, version: 1, rows: false);

        Assert.Equal(RecordWriteOutcome.Written, update.Outcome);
        Assert.Equal((2L, (Guid?)first.Release.Id, (long?)6), await ReadOrderStampAsync(order));
    }

    [Fact]
    public async Task An_update_that_sends_the_lines_stamps_its_release()
    {
        using var folder = LinesFolder("quantity * 2");
        var first = await CompileAsync(folder);
        await ActivateAsync(first);
        var order = await InsertOrderWithLinesAsync(Guid.CreateVersion7(), version: 1, quantities: [1, 2], factor: 2);

        var update = await UpdateNoteAsync(first, first.Release!.Id, order, version: 1, rows: true);

        Assert.Equal(RecordWriteOutcome.Written, update.Outcome);
        Assert.Equal((2L, (Guid?)first.Release.Id, (long?)6), await ReadOrderStampAsync(order));
    }

    private async Task<RecordWriteResult> UpdateNoteAsync(ReleaseCompilationResult compiled, Guid releaseId, Guid orderId, long version, bool rows)
    {
        var model = compiled.Model!;
        var order = model.FindEntity(_orderId)!;
        var line = model.FindEntity(_lineId)!;
        Assert.True(order.TryGetField("note", out var note));
        Assert.True(order.TryGetField("lines", out var lines));
        Assert.True(line.TryGetField("quantity", out var quantity));
        Assert.True(line.TryGetField("amount", out var amount));
        IReadOnlyList<RecordRows> sent = rows
            ? [new RecordRows(lines, line, [[new RecordValue(quantity, 1L), new RecordValue(amount, 2L)]])]
            : [];

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.UpdateAsync(connection, model, order, releaseId, orderId, version, [new RecordValue(note, "changed")], sent, CancellationToken);
        await transaction.CommitAsync(CancellationToken);
        return result;
    }

    private static string Total(string expression) =>
        $$"""{ "name": "total", "type": "integer", "expression": "{{expression}}" }""";

    private string OrderFile(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private string LineFile(string amount) =>
        $$"""
        { "id": "{{_lineId}}", "kind": "entity", "name": "Line", "formatVersion": 1,
          "fields": [ {{Quantity}}, { "name": "amount", "type": "integer", "expression": "{{amount}}" } ] }
        """;

    private TemporaryFolder OrderFolder(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/order.json", OrderFile(fields));

    private TemporaryFolder LinesFolder(string amount) =>
        OrderFolder(Note, Lines, Total("sum(lines, amount)")).With("entities/line.json", LineFile(amount));

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
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);
    }

    private async Task<ActiveRelease?> FindActiveAsync()
    {
        await using var context = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(context).FindByApplicationIdAsync(_applicationId, CancellationToken);
    }

    private string OrderTable => EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));

    private string LineTable => EntityNaming.QualifiedTable(EntityNaming.Table(_lineId));

    private async Task<Guid> InsertOrderAsync(long version, Guid? stamp, long? quantity, long? total = null)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var hasTotal = await ColumnExistsAsync(dataSource, _orderId, "f_total");
        await using var command = dataSource.CreateCommand(
            $"""INSERT INTO {OrderTable} ("id", "version", "computed_release_id", "f_quantity"{(hasTotal ? ", \"f_total\"" : "")}) VALUES (@id, @version, @stamp, @quantity{(hasTotal ? ", @total" : "")})""");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)stamp ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("quantity", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)quantity ?? DBNull.Value });
        if (hasTotal)
        {
            command.Parameters.Add(new NpgsqlParameter("total", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)total ?? DBNull.Value });
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private static async Task<bool> ColumnExistsAsync(NpgsqlDataSource dataSource, Guid entityId, string column)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'entities' AND table_name = @table AND column_name = @column)");
        command.Parameters.AddWithValue("table", EntityNaming.Table(entityId));
        command.Parameters.AddWithValue("column", column);
        return (bool)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    /// <summary>An order and one line per quantity, holding the values a release with that <paramref name="factor"/> stored.</summary>
    private async Task<Guid> InsertOrderWithLinesAsync(Guid stamp, long version, long[] quantities, long factor)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using (var command = dataSource.CreateCommand(
            $"""INSERT INTO {OrderTable} ("id", "version", "computed_release_id", "f_note", "f_total") VALUES (@id, @version, @stamp, 'n', @total)"""))
        {
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("version", version);
            command.Parameters.AddWithValue("stamp", stamp);
            command.Parameters.AddWithValue("total", quantities.Sum() * factor);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        for (var position = 0; position < quantities.Length; position++)
        {
            await using var command = dataSource.CreateCommand(
                $"""INSERT INTO {LineTable} ("id", "owner_id", "position", "f_quantity", "f_amount") VALUES (@id, @owner, @position, @quantity, @amount)""");
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("owner", id);
            command.Parameters.AddWithValue("position", position);
            command.Parameters.AddWithValue("quantity", quantities[position]);
            command.Parameters.AddWithValue("amount", quantities[position] * factor);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return id;
    }

    private async Task<(long Version, Guid? Stamp, long? Total)> ReadOrderStampAsync(Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""SELECT "version", "computed_release_id", "f_total" FROM {OrderTable} WHERE "id" = @id""");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    private async Task<List<long>> ReadAmountsAsync(Guid orderId)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""SELECT "f_amount" FROM {LineTable} WHERE "owner_id" = @owner ORDER BY "position" """);
        command.Parameters.AddWithValue("owner", orderId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var amounts = new List<long>();
        while (await reader.ReadAsync(CancellationToken))
        {
            amounts.Add(reader.GetInt64(0));
        }

        return amounts;
    }

    private async Task<long> CountErrorsAsync() => (await ReadErrorsAsync()).Count;

    private async Task<List<(Guid EntityId, Guid RecordId, string Field, Guid ReleaseId, string Error)>> ReadErrorsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT entity_id, record_id, field, release_id, error FROM axis.computed_field_errors WHERE entity_id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", new[] { _orderId, _lineId });
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var errors = new List<(Guid, Guid, string, Guid, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            errors.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4)));
        }

        return errors;
    }
}
