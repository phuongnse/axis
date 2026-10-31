using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A read that the record API does not do itself shows a record's sensitive values in plain text when it holds the data keys
/// of the tenant: the computed fields that a pending read works out on the spot, and the target records of a
/// <c>references</c> field that an aggregate reads.
/// </summary>
public sealed class SensitiveComputedReadTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();
    private readonly Guid _tagId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private RecordCipher Cipher()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{_tenant}:DataKeys:1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build();
        return new RecordCipher(new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration))), _tenant);
    }

    [Fact]
    public async Task A_pending_read_computes_a_record_that_holds_sensitive_values_and_a_sensitive_computed_field_reads_back_plain()
    {
        var cipher = Cipher();
        using var folder = Folder(amount: "quantity * 2", tag: "concat(title, 'A')");
        var first = await CompileAsync(folder);
        await ActivateAsync(first, limit: 10);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        Assert.True(first.Model!.TryGetEntity("Order", out var firstOrder));
        var created = await RecordCommand.CreateAsync(
            connection,
            _tenant,
            first.Model,
            first.Release!.Id,
            firstOrder,
            new RecordActor("requester", ["clerk"]),
            Body("""{ "values": { "title": "Order", "note": "Secret note", "lines": [{ "quantity": 1, "memo": "First memo" }, { "quantity": 2, "memo": "Second memo" }] } }"""),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);
        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        Assert.Equal("OrderA", created.Record!.Values["tag"]!.GetValue<string>());

        folder.With("entities/line.json", LineFile("quantity * 3")).With("entities/order.json", OrderFile("concat(title, 'B')"));
        var second = await CompileAsync(folder);
        await ActivateAsync(second, limit: 0);
        Assert.Equal(1L, await CountPendingAsync());
        Assert.True(second.Model!.TryGetEntity("Order", out var model));

        var result = await RecordCommand.UpdateAsync(
            connection,
            _tenant,
            second.Model,
            second.Release!.Id,
            model,
            created.Record.Id,
            new RecordActor("requester", ["clerk"]),
            Body("""{ "version": 1, "values": { "title": "Renamed" } }"""),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);

        Assert.Equal(RecordCommandOutcome.Written, result.Outcome);
        var written = result.Record!;
        Assert.Equal("Secret note", written.Values["note"]!.GetValue<string>());
        Assert.Equal("RenamedB", written.Values["tag"]!.GetValue<string>());
        var lines = written.Values["lines"]!.AsArray();
        Assert.Equal([3L, 6L], lines.Select(line => line!["amount"]!.GetValue<long>()));
        Assert.Equal(["First memo", "Second memo"], lines.Select(line => line!["memo"]!.GetValue<string>()));
        Assert.Equal(9L, written.Values["total"]!.GetValue<long>());
        Assert.Equal(1L, await CountPendingAsync());
    }

    [Fact]
    public async Task A_record_set_reads_the_sensitive_value_of_a_references_target_in_plain_text()
    {
        var cipher = Cipher();
        using var folder = Folder(amount: "quantity * 2", tag: "concat(title, 'A')");
        var compiled = await CompileAsync(folder);
        await ActivateAsync(compiled, limit: 10);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        Assert.True(compiled.Model!.TryGetEntity("Tag", out var tag));
        Assert.True(compiled.Model.TryGetEntity("Order", out var order));
        var created = await RecordCommand.CreateAsync(
            connection,
            _tenant,
            compiled.Model,
            compiled.Release!.Id,
            tag,
            new RecordActor("requester", ["clerk"]),
            Body("""{ "values": { "name": "Priority", "secret": "Tag secret" } }"""),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);
        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var stored = new Axis.Data.Records.Record(
            Guid.NewGuid(),
            1,
            new Dictionary<string, JsonNode?> { ["tags"] = new JsonArray(created.Record!.Id.ToString("D")) },
            new Dictionary<string, JsonNode>());

        var sets = await RecordSets.ReadAsync(connection, compiled.Model, order, stored, [], CancellationToken, cipher);

        Assert.Null(sets.Inexact);
        var row = Assert.Single(sets.RowsOf("tags"));
        Assert.True(row.TryGetValue("secret", out var secret));
        Assert.Equal("Tag secret", secret);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordSets.ReadAsync(connection, compiled.Model, order, stored, [], CancellationToken));
    }

    private static RecordBodyReader Body(string json) => _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(json));

    private string OrderFile(string tag) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [
            { "name": "title", "type": "text" },
            { "name": "note", "type": "text", "sensitive": true },
            { "name": "lines", "type": "child-collection", "target": "Line" },
            { "name": "tags", "type": "references", "target": "Tag" },
            { "name": "total", "type": "integer", "expression": "sum(lines, amount)" },
            { "name": "tag", "type": "text", "sensitive": true, "expression": "{{tag}}" } ] }
        """;

    private string LineFile(string amount) =>
        $$"""
        { "id": "{{_lineId}}", "kind": "entity", "name": "Line", "formatVersion": 1, "fields": [
            { "name": "quantity", "type": "integer" },
            { "name": "memo", "type": "text", "sensitive": true },
            { "name": "amount", "type": "integer", "expression": "{{amount}}" } ] }
        """;

    private TemporaryFolder Folder(string amount, string tag) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/order.json", OrderFile(tag))
            .With("entities/line.json", LineFile(amount))
            .With(
                "entities/tag.json",
                $$"""
                { "id": "{{_tagId}}", "kind": "entity", "name": "Tag", "formatVersion": 1, "displayField": "name", "fields": [
                    { "name": "name", "type": "text", "required": true },
                    { "name": "secret", "type": "text", "sensitive": true } ] }
                """)
            .With("roles/clerk.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }""")
            .With(
                "texts/en.json",
                $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "role.clerk": "Clerk" } }""")
            .With(
                "policies/clerk-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "ClerkAccess", "formatVersion": 1, "roles": ["clerk"], "grants": [
                    { "entity": "Order", "actions": ["read", "create", "update"] },
                    { "entity": "Tag", "actions": ["read", "create"] }] }
                """);

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

    private async Task<long> CountPendingAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.pending_computed_fields WHERE entity_id = @entity");
        command.Parameters.AddWithValue("entity", _orderId);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
