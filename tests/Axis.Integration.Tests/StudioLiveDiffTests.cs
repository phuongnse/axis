using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Server.Tests;
using Axis.Studio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The live diff route lists the files that differ from the live release and the revisions made since its first
/// activation that changed each. See docs/reference/authoring-revisions.md#live-diff.
/// </summary>
public sealed class StudioLiveDiffTests : IAsyncLifetime
{
    private const string Host = "a.example.test";

    private const string LiveDiffUrl = "/api/studio/apps/PurchaseRequests/releases/live/diff";

    private const string RuleId = "313225f1-c69f-41bc-b746-12d91f127c39";

    private const string RuleFile = "rules/finance-threshold.json";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task An_accept_and_a_hand_save_after_the_live_release_are_listed_with_who_made_them()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_live_diff", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, revisionsPath: revisions.Path);

        // Creating a client starts the server, which activates the folder as release 1.
        using var author = factory.CreateClient().SignedInAs("author");

        // A hand save to one file.
        var rule = Path.Combine(folder.Path, RuleFile);
        using var patchRequest = RecordApiFixture.Request(
            HttpMethod.Patch,
            $"/api/studio/apps/PurchaseRequests/resources/{RuleId}",
            Host,
            """[{"op":"replace","path":"/expression","value":"20000"}]""",
            "application/json-patch+json");
        patchRequest.Headers.TryAddWithoutValidation("If-Match", $"\"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(rule)))}\"");
        using var patch = await author.SendAsync(patchRequest, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // An accept to a second file: the draft, the accept's publish request and the AI request that owns the draft.
        var writeLock = factory.Services.GetRequiredService<IStudioWriteLock>();
        var recorder = StudioRevisions.For(revisions.Path, folder.Path, "author", "Author User", NullLogger<StudioRevisions>.Instance)!;
        var drafts = StudioDrafts.For(revisions.Path, folder.Path, "author", "Author User", writeLock)!;
        var draft = (await drafts.CreateAsync(recorder, CancellationToken)).Value!;
        var fresh = Guid.NewGuid();
        Assert.Null((await drafts.WriteAsync(draft.Id, new JsonObject { ["id"] = fresh.ToString("D"), ["kind"] = "policy", ["name"] = "Fresh" }, CancellationToken)).Refusal);
        var applicationId = StudioFolder.ReadManifest(folder.Path)!.Id!.Value;
        var publishRequest = Guid.NewGuid();
        var authoringRequest = Guid.NewGuid();
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.publish_requests (id, application_id, kind, status, draft_id, folder, client, requested_by, requested_by_name, requested_at)
            VALUES ('{publishRequest}', '{applicationId}', 'accept', 'succeeded', '{draft.Id}', 'PurchaseRequests', 'agent', 'author', 'Author User', clock_timestamp());
            INSERT INTO axis.authoring_requests (id, application_id, author, text, state, draft_id)
            VALUES ('{authoringRequest}', '{applicationId}', 'author', 'Add a policy.', 'accepted', '{draft.Id}')
            """,
            CancellationToken);
        await using (var held = (await StudioWrites.TryTakeAsync(writeLock, folder.Path, CancellationToken))!)
        {
            foreach (var (file, bytes) in drafts.Apply(draft.Id, StudioFolder.Files(folder.Path)).Value!)
            {
                var target = Path.Combine(folder.Path, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, bytes, CancellationToken);
            }

            Assert.Null(drafts.CommitAccept(draft.Id, "Accept draft", "author", "Author User", "agent", publishRequest).Refusal);
        }

        var diff = await StudioReleaseHistoryTests.GetAsync(author, LiveDiffUrl);

        Assert.Equal(1, diff.GetProperty("release").GetInt32());
        Assert.Equal(JsonValueKind.Null, diff.GetProperty("cursor").ValueKind);
        var resources = diff.GetProperty("resources").EnumerateArray().ToList();
        Assert.Equal(["policies/fresh.json", RuleFile], resources.Select(entry => entry.GetProperty("file").GetString()));
        var added = resources[0];
        Assert.Equal("added", added.GetProperty("change").GetString());
        Assert.Equal(JsonValueKind.Null, added.GetProperty("before").ValueKind);
        Assert.Equal(fresh.ToString("D"), added.GetProperty("id").GetString());
        var accepted = Assert.Single(added.GetProperty("by").EnumerateArray());
        Assert.Equal("Accept draft", accepted.GetProperty("summary").GetString());
        Assert.Equal(publishRequest.ToString("D"), accepted.GetProperty("request").GetString());
        Assert.Equal(authoringRequest, accepted.GetProperty("authoringRequest").GetGuid());
        var saved = resources[1];
        Assert.Equal("changed", saved.GetProperty("change").GetString());
        var by = Assert.Single(saved.GetProperty("by").EnumerateArray());
        Assert.Equal("author", by.GetProperty("userId").GetString());
        Assert.Equal(JsonValueKind.Null, by.GetProperty("request").ValueKind);
        Assert.Equal(JsonValueKind.Null, by.GetProperty("authoringRequest").ValueKind);
        Assert.DoesNotContain(
            resources.SelectMany(entry => entry.GetProperty("by").EnumerateArray()),
            revision => revision.GetProperty("summary").GetString() == "Start of history");
    }

    [Fact]
    public async Task A_server_with_no_live_release_answers_404_and_the_route_refuses_a_person_without_the_author_role_and_a_foreign_cursor()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var connectionString = await _database.CreateDatabaseAsync("studio_live_diff_none", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, revisionsPath: revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        // A cursor the route did not give is refused, whether it is not a cursor or names a commit that is not on main.
        var unknownCommit = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('a', 40) + ":1")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        foreach (var cursor in new[] { "garbage", unknownCommit })
        {
            using var foreign = await author.SendAsync(RecordApiFixture.Request($"{LiveDiffUrl}?cursor={cursor}", Host), CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        }

        await PostgreSqlServer.AdminScalarAsync(connectionString, "DELETE FROM axis.active_releases", CancellationToken);

        using var none = await author.SendAsync(RecordApiFixture.Request(LiveDiffUrl, Host), CancellationToken);
        using var forbidden = await requester.SendAsync(RecordApiFixture.Request(LiveDiffUrl, Host), CancellationToken);
        using var missing = await anonymous.SendAsync(RecordApiFixture.Request(LiveDiffUrl, Host), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
        Assert.Equal("The application has no live release.", await TitleAsync(none));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
    }

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("title").GetString();
    }
}
