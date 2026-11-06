using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using LibGit2Sharp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// Every write through the authoring API becomes a revision in a bare git repository outside the application folder.
/// The platform database holds the folder's write lock, so these tests run on real PostgreSQL.
/// </summary>
public sealed class StudioRevisionTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests";

    private const string RuleId = "313225f1-c69f-41bc-b746-12d91f127c39";

    private const string PolicyId = "2eeb31d5-47d9-4fb9-bd84-eb7ffdc11f12";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_a_changes_call_a_text_write_and_a_delete_each_add_one_commit_after_the_start_of_history()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var created = Guid.NewGuid();

        using var create = await SendAsync(
            client, HttpMethod.Post, $"{Url}/resources", "application/json", $$"""{"id":"{{created}}","kind":"policy","name":"Fresh"}""");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var changes = await SendAsync(
            client,
            HttpMethod.Post,
            $"{Url}/changes",
            "application/json",
            $$"""{"changes":[{"id":"{{RuleId}}","version":"{{Version(folder.Path, "rules/finance-threshold.json")}}","patch":[{"op":"replace","path":"/expression","value":"20000"}]}]}""");
        Assert.Equal(HttpStatusCode.OK, changes.StatusCode);
        using var text = await StudioTextWriteTests.PutAsync(client, "greeting.new", """{"texts":{"en":"Hello","vi":"Xin chào"}}""");
        Assert.Equal(HttpStatusCode.OK, text.StatusCode);
        var file = Directory.EnumerateFiles(Path.Combine(folder.Path, "policies"), "*.json").Single(path => File.ReadAllText(path).Contains(created.ToString()));
        using var delete = await SendAsync(
            client, HttpMethod.Delete, $"{Url}/resources/{created}", null, null, ("If-Match", $"\"{Version(file)}\""));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        using var repository = new Repository(RepositoryPath(revisions.Path, folder.Path));
        var commits = Commits(repository);
        Assert.Equal(
            ["Start of history", "Create policy Fresh", "Change rule FinanceThreshold", "Change text greeting.new", "Delete policy Fresh"],
            commits.Select(commit => commit.MessageShort));
        var start = commits[0];
        Assert.Equal("Axis", start.Author.Name);
        Assert.DoesNotContain("Axis-User-Id", start.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Axis-Client", start.Message, StringComparison.Ordinal);
        Assert.Empty(start.Parents);
        foreach (var (commit, index) in commits.Skip(1).Select((commit, index) => (commit, index + 1)))
        {
            Assert.Equal("Author User", commit.Author.Name);
            Assert.Equal("Author User", commit.Committer.Name);
            Assert.Contains("\nAxis-User-Id: author\nAxis-Client: studio\n", commit.Message, StringComparison.Ordinal);
            Assert.Equal([commits[index - 1].Sha], commit.Parents.Select(parent => parent.Sha));
        }

        Assert.DoesNotContain(".git", Directory.EnumerateFileSystemEntries(folder.Path).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_file_edited_on_disk_between_two_saves_is_committed_as_an_outside_change_before_the_second_save()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        using var first = await PatchAsync(client, RuleId, Version(folder.Path, "rules/finance-threshold.json"), "/expression", "20000");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var texts = Path.Combine(folder.Path, "texts", "en.json");
        File.WriteAllText(texts, File.ReadAllText(texts).Replace("Departments", "Teams", StringComparison.Ordinal));
        using var second = await PatchAsync(client, PolicyId, Version(folder.Path, "policies/employee-access.json"), "/name", "EmployeeAccessRenamed");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var repository = new Repository(RepositoryPath(revisions.Path, folder.Path));
        var commits = Commits(repository);
        Assert.Equal(
            ["Start of history", "Change rule FinanceThreshold", "Outside change", "Change policy EmployeeAccess"],
            commits.Select(commit => commit.MessageShort));
        Assert.Equal("Axis", commits[2].Author.Name);
        Assert.DoesNotContain("Axis-User-Id", commits[2].Message, StringComparison.Ordinal);
        Assert.Equal(["texts/en.json"], Paths(repository, commits[1], commits[2]));
        Assert.Equal(["policies/employee-access.json"], Paths(repository, commits[2], commits[3]));
    }

    [Fact]
    public async Task A_folder_inside_a_git_checkout_gets_its_repository_under_the_revisions_path_and_no_git_folder()
    {
        using var outer = new TemporaryFolder();
        using var revisions = new TemporaryFolder();
        Repository.Init(outer.Path);
        var folder = Path.Combine(outer.Path, "apps", "purchase-requests");
        Copy(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder);
        await using var factory = await CreateFactoryAsync(folder, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        using var response = await PatchAsync(client, RuleId, Version(folder, "rules/finance-threshold.json"), "/expression", "20000");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = Path.Combine(revisions.Path, "purchase-requests.git");
        Assert.True(Repository.IsValid(path));
        using (var repository = new Repository(path))
        {
            Assert.Equal(["Start of history", "Change rule FinanceThreshold"], Commits(repository).Select(commit => commit.MessageShort));
        }

        Assert.False(Directory.Exists(Path.Combine(folder, ".git")));
        Assert.False(File.Exists(Path.Combine(folder, ".git")));
        using var checkout = new Repository(outer.Path);
        Assert.Null(checkout.Head.Tip);
    }

    [Fact]
    public async Task A_write_refused_with_409_or_403_adds_no_commit()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var saved = await PatchAsync(author, RuleId, Version(folder.Path, "rules/finance-threshold.json"), "/expression", "20000");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var path = RepositoryPath(revisions.Path, folder.Path);
        var before = CommitShas(path);
        Assert.Equal(2, before.Count);

        using var stale = await PatchAsync(author, RuleId, new string('0', 64), "/expression", "30000");
        using var forbidden = await PatchAsync(requester, RuleId, Version(folder.Path, "rules/finance-threshold.json"), "/expression", "30000");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(before, CommitShas(path));
    }

    [Fact]
    public async Task The_list_and_the_diff_of_a_revision_match_the_writes_that_made_them()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var rule = Path.Combine(folder.Path, "rules", "finance-threshold.json");
        var original = File.ReadAllText(rule);
        var created = Guid.NewGuid();

        using var patch = await PatchAsync(client, RuleId, Version(rule), "/expression", "20000");
        using var create = await SendAsync(
            client, HttpMethod.Post, $"{Url}/resources", "application/json", $$"""{"id":"{{created}}","kind":"policy","name":"Fresh"}""");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var createdFile = (await ReadAsync(create)).GetProperty("file").GetString()!;

        using var list = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions", null);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = (await ReadAsync(list)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Create policy Fresh", "Change rule FinanceThreshold", "Start of history"], items.Select(item => item.GetProperty("summary").GetString()));
        Assert.Equal(["Author User", "Author User", "Axis"], items.Select(item => item.GetProperty("author").GetString()));
        Assert.Equal(["author", "author", null], items.Select(item => item.GetProperty("userId").GetString()));
        Assert.Equal(["studio", "studio", null], items.Select(item => item.GetProperty("client").GetString()));
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("request").ValueKind));
        Assert.All(items, item => Assert.True(item.GetProperty("time").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.Equal(CommitShas(RepositoryPath(revisions.Path, folder.Path)).AsEnumerable().Reverse(), items.Select(item => item.GetProperty("id").GetString()));

        using var changeDiff = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions/{items[1].GetProperty("id").GetString()}", null);
        Assert.Equal(HttpStatusCode.OK, changeDiff.StatusCode);
        var change = await ReadAsync(changeDiff);
        Assert.Equal("Change rule FinanceThreshold", change.GetProperty("revision").GetProperty("summary").GetString());
        var changed = Assert.Single(change.GetProperty("resources").EnumerateArray());
        Assert.Equal("rules/finance-threshold.json", changed.GetProperty("file").GetString());
        Assert.Equal("changed", changed.GetProperty("change").GetString());
        Assert.Equal(RuleId, changed.GetProperty("id").GetString());
        Assert.Equal("rule", changed.GetProperty("kind").GetString());
        Assert.Equal("FinanceThreshold", changed.GetProperty("name").GetString());
        Assert.Equal(original, changed.GetProperty("before").GetString());
        Assert.Equal(File.ReadAllText(rule), changed.GetProperty("after").GetString());

        using var createDiff = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions/{items[0].GetProperty("id").GetString()}", null);
        var added = Assert.Single((await ReadAsync(createDiff)).GetProperty("resources").EnumerateArray());
        Assert.Equal(createdFile, added.GetProperty("file").GetString());
        Assert.Equal("added", added.GetProperty("change").GetString());
        Assert.Equal(created.ToString(), added.GetProperty("id").GetString());
        Assert.Equal("policy", added.GetProperty("kind").GetString());
        Assert.Equal("Fresh", added.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, added.GetProperty("before").ValueKind);
        Assert.Equal(File.ReadAllText(Path.Combine(folder.Path, createdFile)), added.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Undoing_the_last_revision_puts_the_files_back_and_adds_an_undo_revision()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var rule = Path.Combine(folder.Path, "rules", "finance-threshold.json");
        var original = File.ReadAllBytes(rule);
        using var patch = await PatchAsync(client, RuleId, Version(rule), "/expression", "20000");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.NotEqual(original, File.ReadAllBytes(rule));
        var path = RepositoryPath(revisions.Path, folder.Path);
        var patched = CommitShas(path)[^1];

        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{patched}/undo", null);

        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        var answer = await ReadAsync(undo);
        Assert.Equal(original, File.ReadAllBytes(rule));
        using var repository = new Repository(path);
        var commits = Commits(repository);
        Assert.Equal(["Start of history", "Change rule FinanceThreshold", "Undo Change rule FinanceThreshold"], commits.Select(commit => commit.MessageShort));
        Assert.Equal(commits[^1].Sha, answer.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Array, answer.GetProperty("diagnostics").ValueKind);
        Assert.Contains("\nAxis-User-Id: author\nAxis-Client: studio\n", commits[^1].Message, StringComparison.Ordinal);
        Assert.Equal(commits[0].Tree.Sha, commits[^1].Tree.Sha);
    }

    [Fact]
    public async Task Undoing_a_created_resource_removes_its_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        using var create = await SendAsync(
            client, HttpMethod.Post, $"{Url}/resources", "application/json", $$"""{"id":"{{Guid.NewGuid()}}","kind":"policy","name":"Fresh"}""");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var file = Path.Combine(folder.Path, (await ReadAsync(create)).GetProperty("file").GetString()!);
        Assert.True(File.Exists(file));

        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{CommitShas(RepositoryPath(revisions.Path, folder.Path))[^1]}/undo", null);

        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task An_undo_is_refused_with_the_later_revisions_when_one_of_them_changed_the_same_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var rule = Path.Combine(folder.Path, "rules", "finance-threshold.json");
        using var first = await PatchAsync(client, RuleId, Version(rule), "/expression", "20000");
        using var second = await PatchAsync(client, RuleId, Version(rule), "/expression", "30000");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var path = RepositoryPath(revisions.Path, folder.Path);
        var shas = CommitShas(path);
        var files = File.ReadAllBytes(rule);

        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{shas[1]}/undo", null);

        Assert.Equal(HttpStatusCode.Conflict, undo.StatusCode);
        var problem = await ReadAsync(undo);
        Assert.Equal("A later revision changed the same files. Undo it first.", problem.GetProperty("title").GetString());
        Assert.Equal([shas[2]], problem.GetProperty("revisions").EnumerateArray().Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(files, File.ReadAllBytes(rule));
        Assert.Equal(shas, CommitShas(path));
    }

    [Fact]
    public async Task The_start_of_history_cannot_be_undone_and_an_unknown_revision_is_not_found()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var rule = Path.Combine(folder.Path, "rules", "finance-threshold.json");
        using var empty = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions", null);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Empty((await ReadAsync(empty)).GetProperty("items").EnumerateArray());
        using var noHistory = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{new string('a', 40)}/undo", null);
        Assert.Equal(HttpStatusCode.NotFound, noHistory.StatusCode);
        using var patch = await PatchAsync(client, RuleId, Version(rule), "/expression", "20000");
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var path = RepositoryPath(revisions.Path, folder.Path);
        var shas = CommitShas(path);

        using var start = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{shas[0]}/undo", null);
        using var unknown = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{new string('a', 40)}/undo", null);
        using var malformed = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions/main", null);
        using var unknownDiff = await SendAsync(client, HttpMethod.Get, $"{Url}/revisions/{new string('a', 40)}", null);

        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Equal("The start of history cannot be undone.", (await ReadAsync(start)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownDiff.StatusCode);
        Assert.Equal(shas, CommitShas(path));
    }

    [Fact]
    public async Task A_person_without_the_author_role_gets_403_on_the_list_the_diff_and_the_undo_and_no_revision_is_added()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var saved = await PatchAsync(author, RuleId, Version(folder.Path, "rules/finance-threshold.json"), "/expression", "20000");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var path = RepositoryPath(revisions.Path, folder.Path);
        var before = CommitShas(path);
        var bytes = File.ReadAllBytes(Path.Combine(folder.Path, "rules", "finance-threshold.json"));

        using var list = await SendAsync(requester, HttpMethod.Get, $"{Url}/revisions", null);
        using var diff = await SendAsync(requester, HttpMethod.Get, $"{Url}/revisions/{before[^1]}", null);
        using var undo = await SendAsync(requester, HttpMethod.Post, $"{Url}/revisions/{before[^1]}/undo", null);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, diff.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, undo.StatusCode);
        Assert.Equal(before, CommitShas(path));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder.Path, "rules", "finance-threshold.json")));
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.Clone();
    }

    private static string RepositoryPath(string revisions, string folder) =>
        Path.Combine(revisions, Path.GetFileName(folder) + ".git");

    // The commits of main, oldest first.
    private static List<Commit> Commits(Repository repository) =>
        [.. repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = "refs/heads/main", SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse })];

    private static List<string> CommitShas(string path)
    {
        using var repository = new Repository(path);
        return [.. Commits(repository).Select(commit => commit.Sha)];
    }

    private static List<string> Paths(Repository repository, Commit parent, Commit commit) =>
        [.. repository.Diff.Compare<TreeChanges>(parent.Tree, commit.Tree).Select(change => change.Path)];

    private static string Version(string folder, string file) => Version(Path.Combine(folder, file));

    private static string Version(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, string id, string version, string path, string value) =>
        SendAsync(
            client,
            HttpMethod.Patch,
            $"{Url}/resources/{id}",
            "application/json-patch+json",
            $$"""[{"op":"replace","path":"{{path}}","value":"{{value}}"}]""",
            ("If-Match", $"\"{version}\""));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, string? contentType, string? body = null, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue(contentType!));
        }

        request.Headers.Add("X-Axis-Csrf", "1");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder, string revisions)
    {
        var platform = await database.CreateDatabaseAsync("studio_revisions", CancellationToken);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(platform);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", SampleModules.Path);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:RevisionsPath", revisions);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }
}
