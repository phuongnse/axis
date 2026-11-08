using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Studio;
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
    public async Task The_files_of_a_draft_hold_an_added_resource_and_record_no_version()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var policy = Guid.NewGuid();
        var added = (await author.Drafts.WriteAsync(draft.Id, Policy(policy), CancellationToken)).Value!;
        var before = (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions;

        var files = (await author.Drafts.FilesAsync(draft.Id, CancellationToken)).Value!;

        Assert.Contains(files, file => file.File == added.File);
        Assert.Contains(files, file => file.File == RuleFile);
        Assert.Equal(StudioFolder.Files(folder.Path).Count + 1, files.Count);
        Assert.Equal(before, (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Value!.Versions);
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
    public async Task Conflicts_list_each_used_resource_whose_version_on_main_now_differs_and_not_an_unrelated_change()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var rule = StudioFolder.List(folder.Path).Single(file => file.Id == Guid.Parse(RuleId));
        var unrelated = StudioFolder.List(folder.Path).First(file => file.Kind == "entity");
        var policy = Guid.NewGuid();

        await author.Drafts.ReadAsync(draft.Id, rule.Id, CancellationToken);
        await author.Drafts.WriteAsync(draft.Id, Policy(policy), CancellationToken);
        Assert.Empty(author.Drafts.Conflicts(draft.Id).Value!);

        await File.AppendAllTextAsync(Path.Combine(folder.Path, RuleFile), "\n", CancellationToken);
        await File.AppendAllTextAsync(Path.Combine(folder.Path, unrelated.File), "\n", CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(folder.Path, "policies", "outside.json"), StudioWrites.Serialize(Policy(policy)), CancellationToken);
        author.Revisions.RecordOutside();

        var conflicts = author.Drafts.Conflicts(draft.Id).Value!;
        var expected = new[]
        {
            new StudioDraftConflict(rule.Id, StudioFolder.List(folder.Path).Single(file => file.Id == rule.Id).Version),
            new StudioDraftConflict(policy, StudioFolder.List(folder.Path).Single(file => file.Id == policy).Version),
        };
        Assert.Equal(expected.OrderBy(conflict => conflict.Id), conflicts);
        Assert.DoesNotContain(conflicts, conflict => conflict.Id == unrelated.Id);

        File.Delete(Path.Combine(folder.Path, RuleFile));
        author.Revisions.RecordOutside();
        Assert.Contains(new StudioDraftConflict(rule.Id, null), author.Drafts.Conflicts(draft.Id).Value!);
    }

    [Fact]
    public async Task Apply_returns_the_folders_files_with_the_drafts_changes_and_leaves_the_folder_alone()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var main = StudioFolder.List(folder.Path);
        var rule = main.Single(file => file.Id == Guid.Parse(RuleId));
        var removed = main.First(file => file.Kind == "entity");
        var other = main.First(file => file.Id != rule.Id && file.Id != removed.Id && file.Kind != "application");
        var changed = (JsonObject)JsonNode.Parse(rule.Bytes)!;
        changed["expression"] = "20000";
        var policy = Guid.NewGuid();
        await author.Drafts.WriteAsync(draft.Id, changed, CancellationToken);
        await author.Drafts.WriteAsync(draft.Id, Policy(policy), CancellationToken);
        await author.Drafts.DeleteAsync(draft.Id, removed.Id, CancellationToken);

        await File.AppendAllTextAsync(Path.Combine(folder.Path, other.File), "\n", CancellationToken);
        author.Revisions.RecordOutside();
        var before = Snapshot(folder.Path);

        var applied = author.Drafts.Apply(draft.Id, StudioFolder.Files(folder.Path)).Value!;

        Assert.Equal(StudioWrites.Serialize(changed), applied.Single(file => file.File == rule.File).Bytes);
        Assert.Equal(StudioWrites.Serialize(Policy(policy)), applied.Single(file => file.File == "policies/fresh.json").Bytes);
        Assert.DoesNotContain(applied, file => file.File == removed.File);
        Assert.Equal(File.ReadAllBytes(Path.Combine(folder.Path, other.File)), applied.Single(file => file.File == other.File).Bytes);
        Assert.Equal(applied.Select(file => file.File).Order(StringComparer.Ordinal), applied.Select(file => file.File));
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task Committing_an_accept_adds_one_revision_naming_the_request_and_deletes_the_draft()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var removed = StudioFolder.List(folder.Path).First(file => file.Kind == "entity");
        await author.Drafts.WriteAsync(draft.Id, Policy(Guid.NewGuid()), CancellationToken);
        await author.Drafts.DeleteAsync(draft.Id, removed.Id, CancellationToken);

        var applied = author.Drafts.Apply(draft.Id, StudioFolder.Files(folder.Path)).Value!;
        foreach (var (file, bytes) in applied)
        {
            var target = Path.Combine(folder.Path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, CancellationToken);
        }

        var kept = applied.Select(file => file.File).ToHashSet(StringComparer.Ordinal);
        foreach (var (file, _) in StudioFolder.Files(folder.Path).Where(file => !kept.Contains(file.File)))
        {
            File.Delete(Path.Combine(folder.Path, file));
        }

        var count = author.Revisions.List().Count;
        var requestId = Guid.NewGuid();
        var accepted = author.Drafts.CommitAccept(draft.Id, "Accept draft", "author", "Author User", "agent", requestId).Value!;

        var list = author.Revisions.List();
        Assert.Equal(count + 1, list.Count);
        Assert.Equal(accepted, list[0]);
        Assert.Equal("Accept draft", accepted.Summary);
        Assert.Equal(requestId.ToString("D"), accepted.Request);
        Assert.Equal("agent", accepted.Client);
        Assert.Equal("author", accepted.UserId);
        Assert.Equal("Author User", accepted.Author);
        Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.FindAsync(draft.Id, CancellationToken)).Refusal);
        using var repository = new Repository(Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git"));
        Assert.Null(repository.Refs["refs/heads/drafts/" + draft.Id]);
        Assert.Null(repository.Refs["refs/axis/drafts/" + draft.Id]);
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

        string MainTip()
        {
            using var repository = new Repository(path);
            return repository.Refs["refs/heads/main"].TargetIdentifier;
        }

        var tip = Tip();
        var mainTip = MainTip();

        foreach (var id in new[] { theirs.Id, Guid.NewGuid().ToString("D"), "not-a-guid", "../main" })
        {
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.FindAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.ListAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.FilesAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.ReadAsync(id, rule.Id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.WriteAsync(id, Policy(Guid.NewGuid()), CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.DeleteAsync(id, rule.Id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.DropAsync(id, CancellationToken)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, author.Drafts.Conflicts(id).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, author.Drafts.Apply(id, StudioFolder.Files(folder.Path)).Refusal);
            Assert.Equal(StudioWriteRefusal.NotFound, author.Drafts.CommitAccept(id, "Accept", "author", "Author User", "agent", Guid.NewGuid()).Refusal);
        }

        Assert.Equal(mainTip, MainTip());
        Assert.Equal(tip, Tip());
        using (var repository = new Repository(path))
        {
            Assert.NotNull(repository.Refs["refs/axis/drafts/" + theirs.Id]);
        }

        Assert.Equal(theirs.Id, (await owner.Drafts.FindAsync(theirs.Id, CancellationToken)).Value!.Id);
        Assert.Equal(rule.Version, (await owner.Drafts.ReadAsync(theirs.Id, rule.Id, CancellationToken)).Value!.Version);
    }

    [Fact]
    public async Task The_diff_of_a_draft_lists_each_added_changed_and_removed_file_with_its_json_before_and_after()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var layerFile = "customizations/approvals/RequestApproval.json";
        Directory.CreateDirectory(Path.Combine(folder.Path, "customizations", "approvals"));
        var layerText = Layer("20000");
        await File.WriteAllTextAsync(Path.Combine(folder.Path, layerFile), layerText, CancellationToken);
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));

        var fresh = (await author.Drafts.DiffAsync(draft.Id, null, CancellationToken)).Value!;
        Assert.Empty(fresh.Resources);
        Assert.Null(fresh.Cursor);
        Assert.Equal(draft.Start, fresh.Start);
        Assert.Equal(draft.Start, fresh.Tip);

        var main = StudioFolder.List(folder.Path);
        var rule = main.Single(file => file.Id == Guid.Parse(RuleId));
        var removed = main.First(file => file.Kind == "entity");
        var layer = main.Single(file => file.File == layerFile);
        var changedRule = (JsonObject)JsonNode.Parse(rule.Bytes)!;
        changedRule["expression"] = "20000";
        var changedLayer = (JsonObject)JsonNode.Parse(layer.Bytes)!;
        changedLayer["changes"]!["note"] = "changed";
        var policy = Guid.NewGuid();
        Assert.Null((await author.Drafts.WriteAsync(draft.Id, changedRule, CancellationToken)).Refusal);
        Assert.Null((await author.Drafts.WriteAsync(draft.Id, changedLayer, CancellationToken)).Refusal);
        Assert.Null((await author.Drafts.WriteAsync(draft.Id, Policy(policy), CancellationToken)).Refusal);
        Assert.True((await author.Drafts.DeleteAsync(draft.Id, removed.Id, CancellationToken)).Value);

        var diff = (await author.Drafts.DiffAsync(draft.Id, null, CancellationToken)).Value!;
        var inDraft = (await author.Drafts.ListAsync(draft.Id, CancellationToken)).Value!;
        Assert.Null(diff.Cursor);
        Assert.Equal(draft.Start, diff.Start);
        Assert.NotEqual(draft.Start, diff.Tip);
        Assert.Equal([layerFile, removed.File, "policies/fresh.json", rule.File], diff.Resources.Select(entry => entry.File));
        Assert.Equal(["changed", "removed", "added", "changed"], diff.Resources.Select(entry => entry.Change));

        var layerEntry = diff.Resources[0];
        Assert.Equal("approvals/RequestApproval", layerEntry.Target);
        Assert.Equal(layer.Id.ToString("D"), layerEntry.Id);
        Assert.Equal("layer", layerEntry.Kind);
        Assert.Equal(Encoding.UTF8.GetString(layer.Bytes), layerEntry.Before);
        Assert.Equal(Encoding.UTF8.GetString(inDraft.Single(file => file.Id == layer.Id).Bytes), layerEntry.After);
        Assert.NotEqual(layerEntry.Before, layerEntry.After);

        var removedEntry = diff.Resources[1];
        Assert.Equal(Encoding.UTF8.GetString(removed.Bytes), removedEntry.Before);
        Assert.Null(removedEntry.After);
        Assert.Equal((removed.Id.ToString("D"), removed.Kind, removed.Name), (removedEntry.Id, removedEntry.Kind, removedEntry.Name));
        Assert.Null(removedEntry.Target);

        var addedEntry = diff.Resources[2];
        Assert.Null(addedEntry.Before);
        Assert.Equal(Encoding.UTF8.GetString(inDraft.Single(file => file.Id == policy).Bytes), addedEntry.After);
        Assert.Equal((policy.ToString("D"), "policy", "Fresh"), (addedEntry.Id, addedEntry.Kind, addedEntry.Name));

        var ruleEntry = diff.Resources[3];
        Assert.Equal(Encoding.UTF8.GetString(rule.Bytes), ruleEntry.Before);
        Assert.Equal(Encoding.UTF8.GetString(inDraft.Single(file => file.Id == rule.Id).Bytes), ruleEntry.After);
        Assert.Contains("20000", ruleEntry.After, StringComparison.Ordinal);
        Assert.Null(ruleEntry.Target);
    }

    [Fact]
    public async Task A_diff_over_one_mebibyte_comes_in_pages_that_a_later_write_does_not_shift()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        var author = await NewAuthorAsync(folder.Path, revisions.Path, "author");
        var draft = Created(await author.Drafts.CreateAsync(author.Revisions, CancellationToken));
        var expected = new List<string>();
        for (var number = 1; number <= 6; number++)
        {
            var big = Policy(Guid.NewGuid());
            big["name"] = "Big" + number;
            big["description"] = new string('x', 300_000);
            Assert.Null((await author.Drafts.WriteAsync(draft.Id, big, CancellationToken)).Refusal);
            expected.Add($"policies/big{number}.json");
        }

        var pages = new List<StudioDraftDiff> { (await author.Drafts.DiffAsync(draft.Id, null, CancellationToken)).Value! };
        var late = Policy(Guid.NewGuid());
        late["name"] = "Late";
        Assert.Null((await author.Drafts.WriteAsync(draft.Id, late, CancellationToken)).Refusal);
        while (pages[^1].Cursor is { } cursor)
        {
            pages.Add((await author.Drafts.DiffAsync(draft.Id, cursor, CancellationToken)).Value!);
            Assert.True(pages.Count < 10);
        }

        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, JsonSerializerOptions.Web).Length <= StudioDrafts.DiffPageBytes));
        Assert.All(pages, page => Assert.Equal(pages[0].Tip, page.Tip));
        Assert.Equal(expected, pages.SelectMany(page => page.Resources).Select(entry => entry.File));

        var current = (await author.Drafts.DiffAsync(draft.Id, null, CancellationToken)).Value!;
        Assert.NotEqual(pages[0].Tip, current.Tip);

        var mainTip = pages[0].Start;
        Assert.Equal(StudioWriteRefusal.DoesNotApply, (await author.Drafts.DiffAsync(draft.Id, "garbage", CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.DoesNotApply, (await author.Drafts.DiffAsync(draft.Id, Cursor(mainTip, 1), CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.DoesNotApply, (await author.Drafts.DiffAsync(draft.Id, Cursor(pages[0].Tip, 99), CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.DoesNotApply, (await author.Drafts.DiffAsync(draft.Id, Cursor(pages[0].Tip, 0), CancellationToken)).Refusal);
        Assert.Equal(StudioWriteRefusal.NotFound, (await author.Drafts.DiffAsync(Guid.NewGuid().ToString("D"), null, CancellationToken)).Refusal);
    }

    private static string Cursor(string sha, int index) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sha}:{index}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Layer(string threshold) => $$"""
        {
          "id": "{{Guid.NewGuid():D}}",
          "kind": "layer",
          "formatVersion": 1,
          "target": "approvals/RequestApproval",
          "changes": { "threshold": "{{threshold}}" }
        }
        """;

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
