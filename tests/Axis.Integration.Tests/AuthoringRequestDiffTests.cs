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
/// The request diff route lets any author read what an authoring request changes: the draft's changes in pages while the request has a
/// draft, and the changes of its accept's revision, with titles, once it is accepted. See
/// docs/reference/authoring-requests.md#routes. The accept is built here with the calls the publisher makes, as
/// <see cref="StudioLiveDiffTests"/> does, because the publisher runs in a separate process.
/// </summary>
public sealed class AuthoringRequestDiffTests : IAsyncLifetime
{
    private const string Host = StudioPublishTests.HostA;

    private const string Url = "/api/studio/apps/PurchaseRequests/authoring-requests";

    private const string RuleId = "313225f1-c69f-41bc-b746-12d91f127c39";

    private const string SupplierId = "2deb5acd-dfd8-4c5a-b546-a1aa03df6964";

    private const string NoChanges = "The request has no draft and no accept.";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Another_author_reads_a_ready_requests_draft_diff_in_pages_that_together_give_every_added_changed_and_removed_resource()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var connectionString = await _database.CreateDatabaseAsync("arq_diff_ready", CancellationToken);
        await using var factory = NewFactory(folder.Path, connectionString, revisions.Path);
        using var employee = factory.CreateClient().SignedInAs("employee");
        var (drafts, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);

        var expected = new List<string>();
        for (var number = 1; number <= 6; number++)
        {
            var big = new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("D"),
                ["kind"] = "policy",
                ["name"] = "Big" + number,
                ["description"] = new string('x', 300_000),
            };
            Assert.Null((await drafts.WriteAsync(draft, big, CancellationToken)).Refusal);
            expected.Add($"policies/big{number}.json");
        }

        var rule = (await drafts.ReadAsync(draft, Guid.Parse(RuleId), CancellationToken)).Value!;
        var changed = JsonNode.Parse(rule.Bytes)!.AsObject();
        changed["expression"] = "20000";
        Assert.Null((await drafts.WriteAsync(draft, changed, CancellationToken)).Refusal);
        expected.Add("rules/finance-threshold.json");

        var removed = StudioFolder.List(folder.Path).First(file => file.Kind == "policy");
        Assert.Null((await drafts.DeleteAsync(draft, removed.Id, CancellationToken)).Refusal);
        expected.Add(removed.File);
        expected.Sort(StringComparer.Ordinal);

        var request = await InsertRequestAsync(connectionString, folder.Path, "ready", draft);

        var pages = new List<JsonElement> { await GetAsync(employee, request) };
        while (pages[^1].GetProperty("cursor") is { ValueKind: JsonValueKind.String } cursor)
        {
            pages.Add(await GetAsync(employee, request, cursor.GetString()));
            Assert.True(pages.Count < 10);
        }

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Equal(pages[0].GetProperty("tip").GetString(), page.GetProperty("tip").GetString()));
        var entries = pages.SelectMany(page => page.GetProperty("resources").EnumerateArray()).ToList();
        Assert.Equal(expected, entries.Select(entry => entry.GetProperty("file").GetString()));
        Assert.Equal(
            ["added", "changed", "removed"],
            entries.Select(entry => entry.GetProperty("change").GetString()).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal("changed", entries.Single(entry => entry.GetProperty("id").GetString() == RuleId).GetProperty("change").GetString());
        Assert.Equal("removed", entries.Single(entry => entry.GetProperty("id").GetString() == removed.Id.ToString("D")).GetProperty("change").GetString());

        // A failed request keeps its draft, so it answers the same changes.
        await PostgreSqlServer.AdminScalarAsync(connectionString, $"UPDATE axis.authoring_requests SET state = 'failed' WHERE id = '{request}'", CancellationToken);
        Assert.Equal(pages[0].GetProperty("tip").GetString(), (await GetAsync(employee, request)).GetProperty("tip").GetString());
    }

    [Fact]
    public async Task An_accepted_request_answers_the_changes_of_the_revision_its_accept_made_with_titles_in_one_page()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var connectionString = await _database.CreateDatabaseAsync("arq_diff_accepted", CancellationToken);
        await using var factory = NewFactory(folder.Path, connectionString, revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var employee = factory.CreateClient().SignedInAs("employee");
        var (drafts, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);

        var supplier = (await drafts.ReadAsync(draft, Guid.Parse(SupplierId), CancellationToken)).Value!;
        var changed = JsonNode.Parse(supplier.Bytes)!.AsObject();
        changed["fields"]!.AsArray().Add(JsonNode.Parse("""{ "name": "city", "type": "text", "maxLength": 50 }"""));
        Assert.Null((await drafts.WriteAsync(draft, changed, CancellationToken)).Refusal);
        var fresh = Guid.NewGuid();
        Assert.Null((await drafts.WriteAsync(draft, new JsonObject { ["id"] = fresh.ToString("D"), ["kind"] = "policy", ["name"] = "Fresh" }, CancellationToken)).Refusal);
        var request = await InsertRequestAsync(connectionString, folder.Path, "ready", draft);

        // Before the accept, the request still answers its draft.
        Assert.Equal(2, (await GetAsync(employee, request)).GetProperty("resources").GetArrayLength());

        using var accept = await StudioPublishTests.SendAsync(author, HttpMethod.Post, $"{Url}/{request}/accept", Host);
        Assert.Equal(HttpStatusCode.Accepted, accept.StatusCode);
        using var queued = await StudioPublishTests.ReadAsync(accept);
        var publishRequest = queued.RootElement.GetProperty("id").GetGuid();

        // The publisher writes the draft into the folder and records it as a revision, then marks the accept and the request.
        var writeLock = factory.Services.GetRequiredService<IStudioWriteLock>();
        await using (var held = (await StudioWrites.TryTakeAsync(writeLock, folder.Path, CancellationToken))!)
        {
            foreach (var (file, bytes) in drafts.Apply(draft, StudioFolder.Files(folder.Path)).Value!)
            {
                var target = Path.Combine(folder.Path, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, CancellationToken);
            }

            Assert.Null(drafts.CommitAccept(draft, "Accept draft", "author", "Author User", "studio", publishRequest).Refusal);
        }

        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""
            UPDATE axis.publish_requests SET status = 'succeeded', finished_at = now() WHERE id = '{publishRequest}';
            UPDATE axis.authoring_requests SET state = 'accepted' WHERE id = '{request}'
            """,
            CancellationToken);

        var list = await StudioReleaseHistoryTests.GetAsync(author, "/api/studio/apps/PurchaseRequests/revisions");
        var revisionIds = list.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToList();
        var diff = await GetAsync(employee, request);

        Assert.Equal(JsonValueKind.Null, diff.GetProperty("cursor").ValueKind);
        Assert.Equal(revisionIds[0], diff.GetProperty("tip").GetString());
        Assert.Equal(revisionIds[1], diff.GetProperty("start").GetString());
        var entries = diff.GetProperty("resources").EnumerateArray().ToList();
        Assert.Equal(["entities/supplier.json", "policies/fresh.json"], entries.Select(entry => entry.GetProperty("file").GetString()));
        Assert.Equal(["changed", "added"], entries.Select(entry => entry.GetProperty("change").GetString()));
        Assert.True(entries[0].GetProperty("titles").TryGetProperty("en", out var title));
        Assert.False(string.IsNullOrEmpty(title.GetString()));
        Assert.Equal(JsonValueKind.Null, entries[1].GetProperty("titles").ValueKind);
        Assert.Equal(fresh.ToString("D"), entries[1].GetProperty("id").GetString());

        // An accepted request never gives a cursor, so any cursor is refused.
        using var withCursor = await author.SendAsync(RecordApiFixture.Request($"{Url}/{request}/diff?cursor=garbage", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, withCursor.StatusCode);
    }

    [Fact]
    public async Task A_person_without_the_author_role_is_refused_and_a_request_with_no_draft_and_no_accept_answers_404()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var connectionString = await _database.CreateDatabaseAsync("arq_diff_refused", CancellationToken);
        await using var factory = NewFactory(folder.Path, connectionString, revisions.Path);
        using var employee = factory.CreateClient().SignedInAs("employee");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        var (_, draft) = await NewDraftAsync(factory, revisions.Path, folder.Path);
        var ready = await InsertRequestAsync(connectionString, folder.Path, "ready", draft);
        var dropped = await InsertRequestAsync(connectionString, folder.Path, "dropped", null);
        var noAccept = await InsertRequestAsync(connectionString, folder.Path, "accepted", draft);

        using var forbidden = await requester.SendAsync(RecordApiFixture.Request($"{Url}/{ready}/diff", Host), CancellationToken);
        using var unauthorized = await anonymous.SendAsync(RecordApiFixture.Request($"{Url}/{ready}/diff", Host), CancellationToken);
        using var none = await employee.SendAsync(RecordApiFixture.Request($"{Url}/{dropped}/diff", Host), CancellationToken);
        using var accepted = await employee.SendAsync(RecordApiFixture.Request($"{Url}/{noAccept}/diff", Host), CancellationToken);
        using var unknown = await employee.SendAsync(RecordApiFixture.Request($"{Url}/{Guid.NewGuid()}/diff", Host), CancellationToken);
        using var notAnId = await employee.SendAsync(RecordApiFixture.Request($"{Url}/nothing/diff", Host), CancellationToken);
        using var foreign = await employee.SendAsync(RecordApiFixture.Request($"{Url}/{ready}/diff?cursor=garbage", Host), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal(NoChanges, await TitleAsync(none));
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        Assert.Equal(NoChanges, await TitleAsync(accepted));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    private static WebApplicationFactory<Program> NewFactory(string folder, string connectionString, string revisionsPath) =>
        StudioPublishTests.NewFactory(folder, [("a", Host, connectionString)])
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

    private static async Task<Guid> InsertRequestAsync(string connectionString, string folder, string state, string? draft)
    {
        var id = Guid.NewGuid();
        var applicationId = StudioFolder.ReadManifest(folder)!.Id!.Value;
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id)
            VALUES ('{id}', '{applicationId}', 'author', 'Change something.', '{state}', {(draft is null ? "NULL" : $"'{draft}'")})
            """,
            CancellationToken);
        return id;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid request, string? cursor = null) =>
        await StudioReleaseHistoryTests.GetAsync(
            client, $"{Url}/{request}/diff{(cursor is null ? "" : $"?cursor={Uri.EscapeDataString(cursor)}")}");

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("title").GetString();
    }
}
