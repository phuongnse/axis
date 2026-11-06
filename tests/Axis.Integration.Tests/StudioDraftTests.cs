using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using LibGit2Sharp;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A draft is a branch in the revisions repository that takes writes and deletes as commits from memory. The folder's
/// write lock lives in the platform database, so these tests run on real PostgreSQL.
/// </summary>
public sealed class StudioDraftTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string RuleId = "313225f1-c69f-41bc-b746-12d91f127c39";

    private const string RuleFile = "rules/finance-threshold.json";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_and_deletes_in_a_draft_change_neither_the_folder_nor_main_and_another_draft_does_not_see_them()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var before = Snapshot(folder.Path);

        var first = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var second = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var path = Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git");
        string mainTip;
        using (var repository = new Repository(path))
        {
            var main = (Commit)repository.Refs["refs/heads/main"].ResolveToDirectReference().Target;
            mainTip = main.Sha;
            Assert.Equal("Start of history", main.MessageShort);
            Assert.Equal(mainTip, first.Start);
            Assert.Equal(mainTip, second.Start);
        }

        var main1 = StudioFolder.List(folder.Path);
        var rule = main1.Single(file => file.Id == Guid.Parse(RuleId));
        var removed = main1.First(file => file.Kind == "entity");
        var changed = (JsonObject)JsonNode.Parse(rule.Bytes)!;
        changed["expression"] = "20000";
        var policy = Guid.NewGuid();

        Assert.Null((await author.Drafts.WriteAsync(first.Id, changed, CancellationToken)).Refusal);
        Assert.Null((await author.Drafts.WriteAsync(first.Id, Policy(policy), CancellationToken)).Refusal);
        Assert.True((await author.Drafts.DeleteAsync(first.Id, removed.Id, CancellationToken)).Value);

        Assert.Equal(before, Snapshot(folder.Path));
        using (var repository = new Repository(path))
        {
            Assert.Equal(mainTip, repository.Refs["refs/heads/main"].TargetIdentifier);
            var commits = repository.Commits.QueryBy(new CommitFilter
            {
                IncludeReachableFrom = "refs/heads/drafts/" + first.Id,
                ExcludeReachableFrom = first.Start,
                SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse,
            }).ToList();
            Assert.Equal(
                [$"Change rule {rule.Name}", "Create policy Fresh", $"Delete entity {removed.Name}"],
                commits.Select(commit => commit.MessageShort));
            Assert.Equal(first.Start, commits[0].Parents.Single().Sha);
            Assert.All(commits, commit =>
            {
                Assert.Equal("Author User", commit.Author.Name);
                Assert.Contains("\nAxis-User-Id: author\nAxis-Client: studio\n", commit.Message, StringComparison.Ordinal);
            });
            Assert.Equal(mainTip, repository.Refs["refs/heads/drafts/" + second.Id].TargetIdentifier);
        }

        var inFirst = (await author.Drafts.ListAsync(first.Id, CancellationToken)).Value!;
        Assert.Contains(inFirst, file => file.Id == policy && file.File == "policies/fresh.json");
        Assert.DoesNotContain(inFirst, file => file.Id == removed.Id);
        Assert.Contains("20000", Encoding.UTF8.GetString((await author.Drafts.ReadAsync(first.Id, rule.Id, CancellationToken)).Value!.Bytes), StringComparison.Ordinal);

        var inSecond = (await author.Drafts.ListAsync(second.Id, CancellationToken)).Value!;
        Assert.Equal(main1.Select(file => (file.File, file.Version)), inSecond.Select(file => (file.File, file.Version)));
        Assert.Equal(rule.Version, (await author.Drafts.ReadAsync(second.Id, rule.Id, CancellationToken)).Value!.Version);
        Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.ReadAsync(second.Id, policy, CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.Manifest, (await author.Drafts.DeleteAsync(second.Id, ManifestId(folder.Path), CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.DoesNotApply, (await author.Drafts.WriteAsync(second.Id, new JsonObject { ["kind"] = "policy" }, CancellationToken)).Refusal);
    }

    [Fact]
    public async Task A_draft_records_the_version_a_resource_had_on_main_at_its_first_use()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var rule = StudioFolder.List(folder.Path).Single(file => file.Id == Guid.Parse(RuleId));
        var listed = StudioFolder.List(folder.Path).First(file => file.Kind == "entity");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));

        Assert.Empty(draft.Versions);
        await author.Drafts.ListAsync(draft.Id, CancellationToken);
        Assert.Empty((await author.Drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions);

        await author.Drafts.ReadAsync(draft.Id, rule.Id, CancellationToken);
        var first = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder.Path, RuleFile))));
        Assert.Equal(first, rule.Version);
        Assert.Equal(first, (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions[rule.Id]);

        await File.WriteAllTextAsync(Path.Combine(folder.Path, RuleFile), File.ReadAllText(Path.Combine(folder.Path, RuleFile)) + "\n", CancellationToken);
        author.Revisions.RecordOutside();
        var now = StudioFolder.List(folder.Path).Single(file => file.Id == rule.Id).Version;
        Assert.NotEqual(first, now);

        await author.Drafts.ReadAsync(draft.Id, rule.Id, CancellationToken);
        var changed = (JsonObject)JsonNode.Parse(rule.Bytes)!;
        changed["expression"] = "20000";
        await author.Drafts.WriteAsync(draft.Id, changed, CancellationToken);
        var policy = Guid.NewGuid();
        await author.Drafts.WriteAsync(draft.Id, Policy(policy), CancellationToken);
        await author.Drafts.DeleteAsync(draft.Id, listed.Id, CancellationToken);

        var versions = (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions;
        Assert.Equal(first, versions[rule.Id]);
        Assert.Null(versions[policy]);
        Assert.True(versions.ContainsKey(policy));
        Assert.Equal(listed.Version, versions[listed.Id]);
        Assert.Equal(3, versions.Count);
    }

    [Fact]
    public async Task Dropping_a_draft_deletes_its_branch_and_its_record()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var path = Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git");
        using (var repository = new Repository(path))
        {
            Assert.NotNull(repository.Refs["refs/heads/drafts/" + draft.Id]);
            Assert.NotNull(repository.Refs["refs/axis/drafts/" + draft.Id]);
        }

        Assert.True((await author.Drafts.DropAsync(draft.Id, CancellationToken)).Value);

        using (var repository = new Repository(path))
        {
            Assert.Null(repository.Refs["refs/heads/drafts/" + draft.Id]);
            Assert.Null(repository.Refs["refs/axis/drafts/" + draft.Id]);
            Assert.NotNull(repository.Refs["refs/heads/main"]);
        }

        Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Refusal);
    }

    [Fact]
    public async Task Another_persons_draft_and_an_unknown_id_are_not_found_for_every_operation()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var owner = await NewAuthorAsync(folder.Path, revisions.Path, "other");
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var theirs = Created(await owner.Drafts.CreateAsync(owner.Revisions, CancellationToken));
        var rule = StudioFolder.List(folder.Path).Single(file => file.Id == Guid.Parse(RuleId));
        var path = Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git");
        string Tip()
        {
            using var repository = new Repository(path);
            return repository.Refs["refs/heads/drafts/" + theirs.Id].TargetIdentifier;
        }

        var tip = Tip();

        foreach (var id in new[] { theirs.Id, Guid.NewGuid().ToString("D"), "not-a-guid", "../main" })
        {
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.FindAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.ListAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.ReadAsync(id, rule.Id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.WriteAsync(id, Policy(Guid.NewGuid()), CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.DeleteAsync(id, rule.Id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.DropAsync(id, CancellationToken)).Refusal);
        }

        Assert.Equal(tip, Tip());
        using (var repository = new Repository(path))
        {
            Assert.NotNull(repository.Refs["refs/axis/drafts/" + theirs.Id]);
        }

        Assert.Equal(theirs.Id, (await owner.Drafts.FindAsync(theirs.Id, CancellationToken)).Value!.Id);
        Assert.Equal(rule.Version, (await owner.Drafts.ReadAsync(theirs.Id, rule.Id, CancellationToken)).Value!.Version);
    }

    private async Task<Author> NewAuthorAsync(string folder, string revisionsPath, string userId)
    {
        var writeLock = new PlatformStudioWriteLock(
            NpgsqlDataSource.Create(await database.CreateDatabaseAsync("studio_drafts", CancellationToken)),
            NullLogger<PlatformStudioWriteLock>.Instance);
        return new Author(
            StudioDrafts.For(revisionsPath, folder, userId, userId == "author" ? "Author User" : "Other User", writeLock)!,
            StudioRevisions.For(revisionsPath, folder, userId, userId, NullLogger<StudioRevisions>.Instance)!);
    }

    private static StudioDraft Created(StudioDraftOutcome<StudioDraft> outcome)
    {
        Assert.Null(outcome.Refusal);
        return outcome.Value!;
    }

    private static JsonObject Policy(Guid id) => new() { ["id"] = id.ToString("D"), ["kind"] = "policy", ["name"] = "Fresh" };

    private static Guid ManifestId(string folder) => StudioFolder.ReadManifest(folder)!.Id!.Value;

    // The path and SHA-256 of every file in the folder.
    private static List<(string File, string Version)> Snapshot(string folder) =>
        [.. StudioFolder.Files(folder).Select(file => (file.File, Convert.ToHexStringLower(SHA256.HashData(file.Bytes))))];

    private sealed record Author(StudioDrafts Drafts, StudioRevisions Revisions);
}
