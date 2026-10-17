using System.Text;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The response of an update shows a pending computed field as the active release computes it. An update that
/// leaves out the rows of a collection does not compute them again, so the stored rows stay as they were.
/// </summary>
public sealed class PendingComputedUpdateResponseTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_update_that_leaves_out_the_lines_answers_with_the_line_amounts_and_total_of_the_active_release_and_stores_the_old_amounts()
    {
        using var folder = Folder("quantity * 2");
        var first = await CompileAsync(folder);
        await ActivateAsync(first, limit: 10);
        var order = await InsertOrderAsync(first.Release!.Id, [1, 2], factor: 2);

        folder.With("entities/line.json", LineFile("quantity * 3"));
        var second = await CompileAsync(folder);
        await ActivateAsync(second, limit: 0);
        Assert.Equal(1L, await CountPendingAsync());
        Assert.True(second.Model!.TryGetEntity("Order", out var model));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);

        var result = await RecordCommand.UpdateAsync(
            connection,
            _tenant,
            second.Model,
            second.Release!.Id,
            model,
            order,
            new RecordActor("anna", "Anna", ["clerk"]),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("""{ "version": 1, "values": { "title": "Renamed" } }""")),
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.Written, result.Outcome);
        var written = result.Record!;
        Assert.Equal("Renamed", written.Values["title"]!.GetValue<string>());
        Assert.Equal(2L, written.Version);
        Assert.Equal(
            [3L, 6L],
            written.Values["lines"]!.AsArray().Select(line => line!["amount"]!.GetValue<long>()));
        Assert.Equal(9L, written.Values["total"]!.GetValue<long>());
        Assert.Equal([2L, 4L], await ReadAmountsAsync(order));
        Assert.Equal(1L, await CountPendingAsync());
    }

    private string LineFile(string expression) =>
        $$"""
        { "id": "{{_lineId}}", "kind": "entity", "name": "Line", "formatVersion": 1, "fields": [
            { "name": "quantity", "type": "integer" },
            { "name": "amount", "type": "integer", "expression": "{{expression}}" } ] }
        """;

    private TemporaryFolder Folder(string lineExpression) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With(
                "entities/order.json",
                $$"""
                { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [
                    { "name": "title", "type": "text" },
                    { "name": "lines", "type": "child-collection", "target": "Line" },
                    { "name": "total", "type": "integer", "expression": "sum(lines, amount)" } ] }
                """)
            .With("entities/line.json", LineFile(lineExpression))
            .With("roles/clerk.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }""")
            .With(
                "texts/en.json",
                $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "role.clerk": "Clerk" } }""")
            .With(
                "policies/clerk-access.json",
                $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "ClerkAccess", "formatVersion": 1, "roles": ["clerk"], "grants": [{ "entity": "Order", "actions": ["read", "update"] }] }""");

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        return compiled;
    }

    private async Task ActivateAsync(ReleaseCompilationResult compiled, int limit)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var result = await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(_tenant), OpenInstanceCounter.Instance, limit, CancellationToken);
        Assert.Empty(result.Diagnostics);
    }

    private string OrderTable => EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));

    private string LineTable => EntityNaming.QualifiedTable(EntityNaming.Table(_lineId));

    /// <summary>An order and one line per quantity, stamped with the release and holding the values its <paramref name="factor"/> stored.</summary>
    private async Task<Guid> InsertOrderAsync(Guid stamp, long[] quantities, long factor)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using (var command = dataSource.CreateCommand(
            $"""INSERT INTO {OrderTable} ("id", "version", "computed_release_id", "f_title", "f_total") VALUES (@id, 1, @stamp, 'Order', @total)"""))
        {
            command.Parameters.AddWithValue("id", id);
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

    private async Task<long> CountPendingAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.pending_computed_fields WHERE entity_id = @entity");
        command.Parameters.AddWithValue("entity", _orderId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
