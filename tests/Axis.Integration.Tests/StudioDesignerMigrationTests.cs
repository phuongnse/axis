using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.StudioPublishTests;

namespace Axis.Integration.Tests;

/// <summary>
/// The entity designer saves an entity patch and a <c>renameField</c> migration in one call of the changes route. The
/// next publish keeps the stored records under the new field name. The server and the publisher run together here.
/// </summary>
[Collection("Test scenario runs")]
public sealed class StudioDesignerMigrationTests : IAsyncLifetime
{
    private const string ApplicationUrl = "/api/studio/apps/PurchaseRequests";

    private static readonly Guid _memoId = Guid.Parse("5d0f5b0e-6b0a-4c2e-9a53-3e3a7a6f1c11");

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_entity_patch_and_a_rename_migration_saved_in_one_changes_call_publish_and_keep_the_records_under_the_new_name()
    {
        using var folder = NewFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "entities", "memo.json"), $$"""
            {
              "id": "{{_memoId}}",
              "kind": "entity",
              "name": "Memo",
              "formatVersion": 1,
              "fields": [
                { "name": "title", "type": "text" },
                { "name": "note", "type": "text" }
              ]
            }
            """, CancellationToken);
        var connectionString = await _database.CreateDatabaseAsync("studio_designer_migration", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        await using var publisher = RunPublisher("a", HostA, connectionString);
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_memoId));
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""INSERT INTO {table} ("id", "f_title", "f_note") VALUES ('{Guid.NewGuid()}', 'first', 'kept value')""",
            CancellationToken);
        using var memo = await GetJsonAsync(author, $"{ApplicationUrl}/resources/{_memoId:D}", HostA);
        var version = memo.RootElement.GetProperty("version").GetString();

        var migrationId = Guid.NewGuid();
        var body = $$"""
            { "changes": [
              { "id": "{{_memoId:D}}", "version": "{{version}}", "patch": [ { "op": "replace", "path": "/fields/1/name", "value": "remark" } ] },
              { "create": { "id": "{{migrationId:D}}", "kind": "migration", "name": "MemoChange20261021030405", "formatVersion": 1, "entity": "Memo",
                            "changes": [ { "type": "renameField", "from": "note", "to": "remark" } ] } }
            ] }
            """;
        using var saved = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/changes", HostA, body);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var answer = await ReadAsync(saved);
        Assert.Equal([_memoId.ToString("D"), migrationId.ToString("D")], answer.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.DoesNotContain(answer.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("severity").GetString() == "error");
        Assert.True(File.Exists(Path.Combine(folder.Path, "migrations", "memo-change20261021030405.json")));

        using var accepted = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/publish", HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var finished = await WaitForFinalAsync(author, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal("kept value", await PostgreSqlServer.AdminScalarAsync(connectionString, $"""SELECT "f_remark" FROM {table} WHERE "f_title" = 'first'""", CancellationToken));
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"SELECT count(*) FROM information_schema.columns WHERE table_schema = '{EntityNaming.Schema}' AND table_name = '{EntityNaming.Table(_memoId)}' AND column_name = 'f_note'",
            CancellationToken));
    }

    [Fact]
    public async Task An_entity_patch_to_integer_and_a_convert_migration_saved_in_one_changes_call_publish_and_store_the_integer_value()
    {
        using var folder = NewFolder();
        await File.WriteAllTextAsync(Path.Combine(folder.Path, "entities", "memo.json"), $$"""
            {
              "id": "{{_memoId}}",
              "kind": "entity",
              "name": "Memo",
              "formatVersion": 1,
              "fields": [
                { "name": "title", "type": "text" },
                { "name": "qty", "type": "text" }
              ]
            }
            """, CancellationToken);
        var connectionString = await _database.CreateDatabaseAsync("studio_designer_convert", CancellationToken);
        await using var factory = NewFactory(folder.Path, [("a", HostA, connectionString)]);
        using var author = factory.CreateClient().SignedInAs("author");
        await using var publisher = RunPublisher("a", HostA, connectionString);
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(_memoId));
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""INSERT INTO {table} ("id", "f_title", "f_qty") VALUES ('{Guid.NewGuid()}', 'first', '42')""",
            CancellationToken);
        using var memo = await GetJsonAsync(author, $"{ApplicationUrl}/resources/{_memoId:D}", HostA);
        var version = memo.RootElement.GetProperty("version").GetString();

        var migrationId = Guid.NewGuid();
        var body = $$"""
            { "changes": [
              { "id": "{{_memoId:D}}", "version": "{{version}}", "patch": [ { "op": "replace", "path": "/fields/1/type", "value": "integer" } ] },
              { "create": { "id": "{{migrationId:D}}", "kind": "migration", "name": "MemoChange20261021030406", "formatVersion": 1, "entity": "Memo",
                            "changes": [ { "type": "convert", "field": "qty", "value": "qty" } ] } }
            ] }
            """;
        using var saved = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/changes", HostA, body);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var answer = await ReadAsync(saved);
        Assert.DoesNotContain(answer.RootElement.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("severity").GetString() == "error");

        using var accepted = await SendAsync(author, HttpMethod.Post, $"{ApplicationUrl}/publish", HostA);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var finished = await WaitForFinalAsync(author, accepted.Headers.Location!.OriginalString, HostA);
        Assert.Equal("succeeded", finished.RootElement.GetProperty("status").GetString());
        Assert.Equal(42L, await PostgreSqlServer.AdminScalarAsync(connectionString, $"""SELECT "f_qty" FROM {table} WHERE "f_title" = 'first'""", CancellationToken));
        Assert.Equal("bigint", await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"SELECT data_type FROM information_schema.columns WHERE table_schema = '{EntityNaming.Schema}' AND table_name = '{EntityNaming.Table(_memoId)}' AND column_name = 'f_qty'",
            CancellationToken));
    }
}
