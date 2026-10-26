using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A delete trigger starts a process without a subject when a record is deleted through the record API.
/// <c>AssetDeleted</c> starts <c>AssetRemoved</c> with the tag and cost of the deleted <c>Asset</c>,
/// when <c>100 / cost &gt; 0</c>. <c>CheckFlaggedAssets</c> starts <c>AssetCheck</c> for an asset created
/// flagged, which stays running because the worker does not run. A delete that is refused starts nothing.
/// </summary>
public sealed class DeleteTriggerTests(TriggerApiFixture fixture) : IClassFixture<TriggerApiFixture>
{
    private const string Assets = "/api/apps/TriggerApp/entities/Asset/records";

    private const string AssetNotes = "/api/apps/TriggerApp/entities/AssetNote/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_delete_starts_the_process_once_with_parameters_from_the_deleted_record_in_the_same_transaction()
    {
        await fixture.RunUntilIdleAsync(CancellationToken);
        var id = await CreateAssetAsync("""{ "tag": "laptop", "cost": 50 }""");

        using var deleted = await SendAsync(HttpMethod.Delete, $"{Assets}/{id:D}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync($"SELECT count(*) FROM {AssetTable} WHERE {Column("id")} = @id", ("id", id)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE process_id = @process AND subject_id IS NULL AND subject_entity_id IS NULL AND release_id = @release
              AND state = 'running' AND parameters ->> 'tag' = 'laptop' AND (parameters ->> 'cost')::numeric = 50
            """,
            ("process", fixture.ProcessId("AssetRemoved")), ("release", fixture.ReleaseId)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records s
            JOIN axis.process_instances i ON i.id = s.process_instance_id
            JOIN axis.audit_records d ON d.record_id = @record AND d.action = 'record.deleted'
            WHERE s.action = 'process.started' AND i.process_id = @process AND i.parameters ->> 'tag' = 'laptop'
              AND s.actor = 'system' AND s.details ->> 'trigger' = 'AssetDeleted'
              AND s.xmin::text = d.xmin::text
            """,
            ("record", id), ("process", fixture.ProcessId("AssetRemoved"))));
    }

    [Fact]
    public async Task A_delete_of_a_referenced_record_is_a_conflict_and_starts_nothing()
    {
        await fixture.RunUntilIdleAsync(CancellationToken);
        var id = await CreateAssetAsync("""{ "tag": "desk", "cost": 20 }""");
        using var note = await SendAsync(HttpMethod.Post, AssetNotes, $$"""{ "values": { "asset": "{{id:D}}", "text": "Wobbly" } }""");
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        var before = await RemovedCountAsync();

        using var deleted = await SendAsync(HttpMethod.Delete, $"{Assets}/{id:D}");

        Assert.Equal(HttpStatusCode.Conflict, deleted.StatusCode);
        Assert.Equal(before, await RemovedCountAsync());
        Assert.Equal(1L, await fixture.CountAsync($"SELECT count(*) FROM {AssetTable} WHERE {Column("id")} = @id", ("id", id)));
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(id.ToString("D"), StringComparison.Ordinal) && entry.Contains("AssetDeleted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, "the when is not true")]
    [InlineData(0, "the when failed: DivisionByZero")]
    public async Task A_when_that_is_false_or_fails_starts_nothing_logs_the_skip_and_the_delete_commits(int cost, string reason)
    {
        await fixture.RunUntilIdleAsync(CancellationToken);
        var id = await CreateAssetAsync($$"""{ "tag": "chair{{cost}}", "cost": {{cost}} }""");
        var before = await RemovedCountAsync();

        using var deleted = await SendAsync(HttpMethod.Delete, $"{Assets}/{id:D}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync($"SELECT count(*) FROM {AssetTable} WHERE {Column("id")} = @id", ("id", id)));
        Assert.Equal(before, await RemovedCountAsync());

        // The create of the record logged a skip of CheckFlaggedAssets, since the asset is not flagged.
        var skipped = Assert.Single(
            fixture.Logs.Entries,
            entry => entry.Contains(id.ToString("D"), StringComparison.Ordinal) && entry.Contains("AssetDeleted", StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skipped, StringComparison.Ordinal);
        Assert.Contains("AssetRemoved", skipped, StringComparison.Ordinal);
        Assert.Contains(reason, skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_that_is_refused_starts_nothing_and_logs_no_skip()
    {
        await fixture.RunUntilIdleAsync(CancellationToken);
        var running = await CreateAssetAsync("""{ "tag": "scanner", "cost": 10, "flagged": true }""");
        var locked = await CreateAssetAsync("""{ "tag": "locked", "cost": 10 }""");
        var before = await RemovedCountAsync();
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE subject_id = @id AND process_id = @process AND state = 'running'",
            ("id", running), ("process", fixture.ProcessId("AssetCheck"))));

        using var conflict = await SendAsync(HttpMethod.Delete, $"{Assets}/{running:D}");
        using var outside = await SendAsync(HttpMethod.Delete, $"{Assets}/{locked:D}");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(before, await RemovedCountAsync());
        Assert.Equal(2L, await fixture.CountAsync(
            $"SELECT count(*) FROM {AssetTable} WHERE {Column("id")} = ANY(@ids)", ("ids", new[] { running, locked })));
        foreach (var id in new[] { running, locked })
        {
            Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(id.ToString("D"), StringComparison.Ordinal) && entry.Contains("AssetDeleted", StringComparison.Ordinal));
        }
    }

    private static string Column(string field) => EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private string AssetTable
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Asset", out var asset));
            return EntityNaming.QualifiedTable(EntityNaming.Table(asset.Id));
        }
    }

    private Task<long> RemovedCountAsync() =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", fixture.ProcessId("AssetRemoved")));

    private async Task<Guid> CreateAssetAsync(string values)
    {
        using var response = await SendAsync(HttpMethod.Post, Assets, $$"""{ "values": {{values}} }""");
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null)
    {
        using var client = fixture.CreateClient().SignedInAs("cara");
        using var request = Request(method, path, TriggerApiFixture.Host, body);
        return await client.SendAsync(request, CancellationToken);
    }
}
