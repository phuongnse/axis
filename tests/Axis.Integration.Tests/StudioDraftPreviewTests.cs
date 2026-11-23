using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Server.Tests;
using Axis.Studio;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The draft preview route stores a publish request with target <c>preview</c> from the files of the caller's own draft, with the
/// draft as <c>draft_id</c>. It answers 404 for another person's draft and 409 for a tenant without a preview tenant, and stores
/// nothing then. See docs/reference/authoring-requests.md#routes. The publisher runs the request, as for any preview, so only the
/// stored request is checked here.
/// </summary>
public sealed class StudioDraftPreviewTests : IAsyncLifetime
{
    private const string Host = StudioPublishTests.HostA;

    private const string SupplierId = "2deb5acd-dfd8-4c5a-b546-a1aa03df6964";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Another_author_gets_404_for_the_draft_and_nothing_is_stored()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var main = await _database.CreateDatabaseAsync("sdp_other_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("sdp_other_preview", CancellationToken);
        await using var factory = NewFactory(folder.Path, main, preview, revisions.Path);
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var nobody = factory.CreateClient().SignedInAs("requester");
        var (_, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);

        using var other = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, Url(draft), Host);
        using var unknown = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, Url(Guid.NewGuid().ToString("D")), Host);
        using var notAnId = await StudioPublishTests.SendAsync(employee, HttpMethod.Post, Url("nothing"), Host);
        using var denied = await StudioPublishTests.SendAsync(nobody, HttpMethod.Post, Url(draft), Host);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(StudioRevisionEndpoints.NoDraft, (await StudioPublishTests.ReadAsync(other)).RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(main));
    }

    [Fact]
    public async Task A_tenant_without_a_preview_tenant_gets_409_and_nothing_is_stored()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var main = await _database.CreateDatabaseAsync("sdp_none", CancellationToken);
        await using var factory = NewFactory(folder.Path, main, null, revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        var (_, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);

        using var refused = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url(draft), Host);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("This tenant has no preview tenant.", (await StudioPublishTests.ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        Assert.Equal(0L, await StudioPublishTests.CountRequestsAsync(main));
    }

    [Fact]
    public async Task The_authors_draft_queues_a_preview_publish_of_its_tip_and_the_request_answers_its_draft()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var main = await _database.CreateDatabaseAsync("sdp_main", CancellationToken);
        var preview = await _database.CreateDatabaseAsync("sdp_preview", CancellationToken);
        await using var factory = NewFactory(folder.Path, main, preview, revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        var (drafts, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);
        var supplier = (await drafts.ReadAsync(draft, Guid.Parse(SupplierId), CancellationToken)).Value!;
        var changed = JsonNode.Parse(supplier.Bytes)!.AsObject();
        changed["fields"]!.AsArray().Add(JsonNode.Parse("""{ "name": "city", "type": "text", "maxLength": 50 }"""));
        Assert.Null((await drafts.WriteAsync(draft, changed, CancellationToken)).Refusal);

        using var accepted = await StudioPublishTests.SendAsync(author, HttpMethod.Post, Url(draft), Host);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var request = await StudioPublishTests.ReadAsync(accepted);
        var id = request.RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"/api/studio/apps/PurchaseRequests/requests/{id}", accepted.Headers.Location?.OriginalString);
        Assert.Equal("publish", request.RootElement.GetProperty("kind").GetString());
        Assert.Equal("preview", request.RootElement.GetProperty("target").GetString());
        Assert.Equal("queued", request.RootElement.GetProperty("status").GetString());
        Assert.Equal(1L, await StudioPublishTests.CountRequestsAsync(main));
        Assert.Equal(draft, (await PostgreSqlServer.AdminScalarAsync(main, $"SELECT draft_id::text FROM axis.publish_requests WHERE id = '{id}'", CancellationToken))!.ToString());

        // The snapshot is the draft's files, not the working folder's.
        var files = (await drafts.FilesAsync(draft, CancellationToken)).Value!;
        var stored = (string)(await PostgreSqlServer.AdminScalarAsync(main, $"SELECT snapshot::text FROM axis.publish_requests WHERE id = '{id}'", CancellationToken))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(StudioPublishEndpoints.SnapshotJson(StudioFolder.Snapshot(files))), JsonNode.Parse(stored)));
        Assert.DoesNotContain(
            StudioFolder.Snapshot(folder.Path),
            file => file.File == "entities/supplier.json" && stored.Contains(file.Version, StringComparison.Ordinal));

        // The authoring request answers its draft, and null while it has none.
        var withDraft = await InsertRequestAsync(main, folder.Path, draft);
        var withoutDraft = await InsertRequestAsync(main, folder.Path, null);
        Assert.Equal(draft, (await ReadRequestAsync(author, withDraft)).GetProperty("draft").GetString());
        Assert.Equal(JsonValueKind.Null, (await ReadRequestAsync(author, withoutDraft)).GetProperty("draft").ValueKind);
    }

    private static string Url(string draft) => $"/api/studio/apps/PurchaseRequests/drafts/{draft}/preview";

    private static WebApplicationFactory<Program> NewFactory(string folder, string main, string? preview, string revisionsPath) =>
        StudioPublishTests.NewFactory(folder, [("a", Host, main)], preview)
            .WithWebHostBuilder(builder => builder.UseSetting("Studio:RevisionsPath", revisionsPath));

    // The author's draft. It is made after a client has started the server, so the folder is active and has its history.
    private static async Task<(StudioDrafts Drafts, string Draft)> NewDraftAsync(
        WebApplicationFactory<Program> factory, string revisionsPath, string folder)
    {
        using var starting = factory.CreateClient();
        var writeLock = factory.Services.GetRequiredService<IStudioWriteLock>();
        var recorder = StudioRevisions.For(revisionsPath, folder, "author", "Author User", NullLogger<StudioRevisions>.Instance)!;
        var drafts = StudioDrafts.For(revisionsPath, folder, "author", "Author User", writeLock)!;
        return (drafts, (await drafts.CreateAsync(recorder, CancellationToken)).Value!.Id);
    }

    private static async Task<Guid> InsertRequestAsync(string connectionString, string folder, string? draft)
    {
        var id = Guid.NewGuid();
        var applicationId = StudioFolder.ReadManifest(folder)!.Id!.Value;
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id)
            VALUES ('{id}', '{applicationId}', 'author', 'Change something.', 'ready', {(draft is null ? "NULL" : $"'{draft}'")})
            """,
            CancellationToken);
        return id;
    }

    private static async Task<JsonElement> ReadRequestAsync(HttpClient client, Guid request) =>
        await StudioReleaseHistoryTests.GetAsync(client, $"/api/studio/apps/PurchaseRequests/authoring-requests/{request}");
}
