using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Axis.Studio;
using Axis.Tenancy;
using LibGit2Sharp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The package file routes and the run inputs routes of the authoring API, see <c>docs/reference/studio-packages.md#the-authoring-api</c>. A write takes the
/// write lock in the platform database, so these tests run on real PostgreSQL. The identity <c>developer</c> holds the author and
/// developer roles, <c>author</c> holds the author role only and <c>outsider</c> holds the developer role only. The tenant's
/// application packages switch is on in the host unless a test turns it off, see <see cref="CreateFactoryAsync"/>. They also cover
/// <c>studioDeveloper</c> of <c>GET /api/me</c>, see <c>docs/reference/security.md#roles-and-the-current-user</c>.
/// </summary>
public sealed class StudioPackageFileTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests";

    private const string FileUrl = $"{Url}/packages/x/files/adapter.py";

    private const string MoveUrl = $"{Url}/packages/x/move";

    private const string RunInputsUrl = $"{Url}/packages/x/run-inputs";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_developer_creates_and_replaces_a_package_file_and_a_stale_version_is_refused_and_the_undo_puts_the_earlier_bytes_back()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");

        using var create = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v1')\n"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        byte[] first = [.. "print('v1')\n"u8];
        Assert.Equal(first, File.ReadAllBytes(adapter));
        var firstVersion = Version(first);
        Assert.Equal($"\"{firstVersion}\"", create.Headers.ETag?.Tag);
        using (var created = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken))
        {
            Assert.Equal(firstVersion, created.RootElement.GetProperty("version").GetString());
            Assert.Equal(JsonValueKind.Array, created.RootElement.GetProperty("diagnostics").ValueKind);
        }

        using var createAgain = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v3')\n"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Conflict, createAgain.StatusCode);
        Assert.Equal(first, File.ReadAllBytes(adapter));

        using var replace = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v2')\n"}""", ("If-Match", $"\"{firstVersion}\""));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        byte[] second = [.. "print('v2')\n"u8];
        Assert.Equal(second, File.ReadAllBytes(adapter));

        using var stale = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v4')\n"}""", ("If-Match", $"\"{firstVersion}\""));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(second, File.ReadAllBytes(adapter));

        using var read = await client.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal($"\"{Version(second)}\"", read.Headers.ETag?.Tag);
        using (var body = await JsonDocument.ParseAsync(await read.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken))
        {
            Assert.Equal("print('v2')\n", body.RootElement.GetProperty("content").GetString());
            Assert.Equal("x", body.RootElement.GetProperty("package").GetString());
            Assert.Equal("adapter.py", body.RootElement.GetProperty("path").GetString());
        }

        using var list = await client.GetAsync(new Uri($"{Url}/package-files", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using (var items = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken))
        {
            var item = Assert.Single(items.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal("x", item.GetProperty("package").GetString());
            Assert.Equal("adapter.py", item.GetProperty("path").GetString());
            Assert.Equal(Version(second), item.GetProperty("version").GetString());
        }

        using var revisionList = await client.GetAsync(new Uri($"{Url}/revisions", UriKind.Relative), CancellationToken);
        using var listed = await JsonDocument.ParseAsync(await revisionList.Content.ReadAsStreamAsync(CancellationToken), cancellationToken: CancellationToken);
        var latest = listed.RootElement.GetProperty("items")[0];
        Assert.Equal("Change package file packages/x/adapter.py", latest.GetProperty("summary").GetString());
        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{latest.GetProperty("id").GetString()}/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(first, File.ReadAllBytes(adapter));

        using var delete = await SendAsync(client, HttpMethod.Delete, FileUrl, null, ("If-Match", $"\"{firstVersion}\""));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False(File.Exists(adapter));
        using var gone = await client.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        using var repository = new Repository(Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git"));
        Assert.Equal(
            ["Start of history", "Create package file packages/x/adapter.py", "Change package file packages/x/adapter.py", "Undo Change package file packages/x/adapter.py", "Delete package file packages/x/adapter.py"],
            repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = "refs/heads/main", SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse })
                .Select(commit => commit.MessageShort));
    }

    [Fact]
    public async Task A_write_without_the_developer_role_or_the_author_role_answers_403_and_leaves_the_file_unchanged_while_an_author_reads()
    {
        using var folder = StudioPublishTests.NewFolder();
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");
        Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
        byte[] before = [.. "print('v1')\n"u8];
        File.WriteAllBytes(adapter, before);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var ifMatch = ("If-Match", $"\"{Version(before)}\"");

        foreach (var identity in new[] { "author", "outsider" })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var put = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v2')\n"}""", ifMatch);
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
            using var delete = await SendAsync(client, HttpMethod.Delete, FileUrl, null, ifMatch);
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            Assert.Equal(before, File.ReadAllBytes(adapter));
        }

        using var author = factory.CreateClient().SignedInAs("author");
        using var list = await author.GetAsync(new Uri($"{Url}/package-files", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var read = await author.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task A_write_while_the_application_packages_switch_is_off_answers_403_and_leaves_the_file_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");
        Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
        byte[] before = [.. "print('v1')\n"u8];
        File.WriteAllBytes(adapter, before);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: false);
        using var client = factory.CreateClient().SignedInAs("developer");
        var ifMatch = ("If-Match", $"\"{Version(before)}\"");

        using var put = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"print('v2')\n"}""", ifMatch);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        using var create = await SendAsync(client, HttpMethod.Put, $"{Url}/packages/x/files/new.py", """{"content":"x"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var delete = await SendAsync(client, HttpMethod.Delete, FileUrl, null, ifMatch);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        Assert.Equal(before, File.ReadAllBytes(adapter));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "new.py")));
        using var read = await client.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Me_answers_studioDeveloper_for_an_author_with_the_developer_role_while_the_switch_is_on()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");

        using var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await ReadAsync(response);
        Assert.True(me.GetProperty("studio").GetBoolean());
        Assert.True(me.GetProperty("studioDeveloper").GetBoolean());
    }

    [Fact]
    public async Task Me_answers_no_studioDeveloper_without_the_developer_role_without_the_author_role_or_with_the_switch_off()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var on = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        await using var off = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: false);

        foreach (var (factory, identity) in new[] { (on, "author"), (on, "outsider"), (off, "developer") })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await ReadAsync(response)).GetProperty("studioDeveloper").GetBoolean(), identity);
        }
    }

    [Fact]
    public async Task A_bad_path_and_a_missing_precondition_are_refused_and_write_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");

        using var hidden = await SendAsync(client, HttpMethod.Put, $"{Url}/packages/x/files/.hidden", """{"content":"x"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
        using var noHeader = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":"x"}""");
        Assert.Equal((HttpStatusCode)428, noHeader.StatusCode);
        using var notJson = await SendAsync(client, HttpMethod.Put, FileUrl, "x", ("If-None-Match", "*"), contentType: "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notJson.StatusCode);
        using var badBody = await SendAsync(client, HttpMethod.Put, FileUrl, """{"content":1}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.BadRequest, badBody.StatusCode);
        using var missing = await client.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var deleteMissing = await SendAsync(client, HttpMethod.Delete, FileUrl, null, ("If-Match", "\"x\""));
        Assert.Equal(HttpStatusCode.NotFound, deleteMissing.StatusCode);

        Assert.False(Directory.Exists(Path.Combine(folder.Path, "packages")));
    }

    [Fact]
    public async Task A_developer_writes_a_package_file_in_a_draft_and_the_diff_lists_it_while_the_folder_and_main_stay_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");
        Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
        byte[] before = [.. "print('v1')\n"u8];
        File.WriteAllBytes(adapter, before);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var draft = await CreateDraftAsync(factory, folder.Path, revisions.Path, "developer");
        var mainTip = MainTip(revisions.Path, folder.Path);
        using var client = factory.CreateClient().SignedInAs("developer");
        var draftUrl = $"{FileUrl}?draft={draft}";

        using var replace = await SendAsync(client, HttpMethod.Put, draftUrl, """{"content":"print('v2')\n"}""", ("If-Match", $"\"{Version(before)}\""));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        byte[] after = [.. "print('v2')\n"u8];
        Assert.Equal($"\"{Version(after)}\"", replace.Headers.ETag?.Tag);
        var written = await ReadAsync(replace);
        Assert.Equal(Version(after), written.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, written.GetProperty("diagnostics").ValueKind);
        using var create = await SendAsync(client, HttpMethod.Put, $"{Url}/packages/x/files/new.py?draft={draft}", """{"content":"x"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        Assert.Equal(before, File.ReadAllBytes(adapter));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "new.py")));
        Assert.Equal(mainTip, MainTip(revisions.Path, folder.Path));

        using var diff = await SendAsync(client, HttpMethod.Get, $"{Url}/drafts/{draft}/diff", null);
        var entries = (await ReadAsync(diff)).GetProperty("resources").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("file").GetString()!, entry => entry.GetProperty("change").GetString());
        Assert.Equal(new Dictionary<string, string?> { ["packages/x/adapter.py"] = "changed", ["packages/x/new.py"] = "added" }, entries);

        using var read = await client.GetAsync(new Uri(draftUrl, UriKind.Relative), CancellationToken);
        Assert.Equal("print('v2')\n", (await ReadAsync(read)).GetProperty("content").GetString());
        using var folderRead = await client.GetAsync(new Uri(FileUrl, UriKind.Relative), CancellationToken);
        Assert.Equal("print('v1')\n", (await ReadAsync(folderRead)).GetProperty("content").GetString());
        using var list = await client.GetAsync(new Uri($"{Url}/package-files?draft={draft}", UriKind.Relative), CancellationToken);
        Assert.Equal(
            ["adapter.py", "new.py"],
            (await ReadAsync(list)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()));

        using var delete = await SendAsync(client, HttpMethod.Delete, draftUrl, null, ("If-Match", $"\"{Version(after)}\""));
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        using var gone = await client.GetAsync(new Uri(draftUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(adapter));
        Assert.Equal(mainTip, MainTip(revisions.Path, folder.Path));
    }

    [Fact]
    public async Task A_draft_write_with_a_stale_version_answers_409_and_leaves_the_draft_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");
        Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
        byte[] before = [.. "print('v1')\n"u8];
        File.WriteAllBytes(adapter, before);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var draft = await CreateDraftAsync(factory, folder.Path, revisions.Path, "developer");
        using var client = factory.CreateClient().SignedInAs("developer");
        var draftUrl = $"{FileUrl}?draft={draft}";

        using var stale = await SendAsync(client, HttpMethod.Put, draftUrl, """{"content":"print('v2')\n"}""", ("If-Match", "\"stale\""));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var exists = await SendAsync(client, HttpMethod.Put, draftUrl, """{"content":"print('v2')\n"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Conflict, exists.StatusCode);
        using var staleDelete = await SendAsync(client, HttpMethod.Delete, draftUrl, null, ("If-Match", "\"stale\""));
        Assert.Equal(HttpStatusCode.Conflict, staleDelete.StatusCode);

        using var read = await client.GetAsync(new Uri(draftUrl, UriKind.Relative), CancellationToken);
        Assert.Equal("print('v1')\n", (await ReadAsync(read)).GetProperty("content").GetString());
        using var diff = await SendAsync(client, HttpMethod.Get, $"{Url}/drafts/{draft}/diff", null);
        Assert.Empty((await ReadAsync(diff)).GetProperty("resources").EnumerateArray());
        Assert.Equal(before, File.ReadAllBytes(adapter));
    }

    [Fact]
    public async Task A_draft_route_answers_404_for_another_persons_draft_or_an_unknown_draft_and_changes_no_draft()
    {
        using var folder = StudioPublishTests.NewFolder();
        var adapter = Path.Combine(folder.Path, "packages", "x", "adapter.py");
        Directory.CreateDirectory(Path.GetDirectoryName(adapter)!);
        byte[] before = [.. "print('v1')\n"u8];
        File.WriteAllBytes(adapter, before);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var mine = await CreateDraftAsync(factory, folder.Path, revisions.Path, "developer");
        var theirs = await CreateDraftAsync(factory, folder.Path, revisions.Path, "other");
        using var client = factory.CreateClient().SignedInAs("developer");
        var ifMatch = ("If-Match", $"\"{Version(before)}\"");

        foreach (var draft in new[] { theirs, Guid.NewGuid().ToString("D") })
        {
            var url = $"{FileUrl}?draft={draft}";
            using var put = await SendAsync(client, HttpMethod.Put, url, """{"content":"print('v2')\n"}""", ifMatch);
            Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
            using var delete = await SendAsync(client, HttpMethod.Delete, url, null, ifMatch);
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
            using var read = await client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            using var list = await client.GetAsync(new Uri($"{Url}/package-files?draft={draft}", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        }

        // The draft's owner reads the file as it is, so neither draft has changed.
        using var other = factory.CreateClient().SignedInAs("other");
        foreach (var (draft, person) in new[] { (mine, client), (theirs, other) })
        {
            using var diff = await SendAsync(person, HttpMethod.Get, $"{Url}/drafts/{draft}/diff", null);
            Assert.Empty((await ReadAsync(diff)).GetProperty("resources").EnumerateArray());
        }

        Assert.Equal(before, File.ReadAllBytes(adapter));
    }

    [Fact]
    public async Task A_draft_write_without_the_developer_role_or_with_the_switch_off_answers_403_and_leaves_the_draft_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var off = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: false);
        var offDraft = await CreateDraftAsync(off, folder.Path, revisions.Path, "developer");
        using var disabled = off.CreateClient().SignedInAs("developer");
        using var switchedOff = await SendAsync(disabled, HttpMethod.Put, $"{FileUrl}?draft={offDraft}", """{"content":"x"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Forbidden, switchedOff.StatusCode);
        await using var on = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var draft = await CreateDraftAsync(on, folder.Path, revisions.Path, "author");
        using var author = on.CreateClient().SignedInAs("author");
        using var put = await SendAsync(author, HttpMethod.Put, $"{FileUrl}?draft={draft}", """{"content":"x"}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        using var delete = await SendAsync(author, HttpMethod.Delete, $"{FileUrl}?draft={draft}", null, ("If-Match", "\"x\""));
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        using var diff = await SendAsync(author, HttpMethod.Get, $"{Url}/drafts/{draft}/diff", null);
        Assert.Empty((await ReadAsync(diff)).GetProperty("resources").EnumerateArray());
    }

    [Fact]
    public async Task A_developer_reads_no_run_inputs_before_a_save_then_saves_them_with_if_none_match_and_reads_the_same_items_and_version()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var file = Path.Combine(folder.Path, "run-inputs", "x.json");

        using var before = await client.GetAsync(new Uri(RunInputsUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Null(before.Headers.ETag);
        Assert.Empty((await ReadAsync(before)).GetProperty("items").EnumerateArray());

        const string items = """{"items":[{"name":"Small","operation":"echo","input":{"text":"hi"}},{"name":"Empty","operation":"echo","input":null}]}""";
        using var save = await SendAsync(client, HttpMethod.Put, RunInputsUrl, items, ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var version = Version(File.ReadAllBytes(file));
        Assert.Equal($"\"{version}\"", save.Headers.ETag?.Tag);
        Assert.Equal(version, (await ReadAsync(save)).GetProperty("version").GetString());

        using var after = await client.GetAsync(new Uri(RunInputsUrl, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal($"\"{version}\"", after.Headers.ETag?.Tag);
        var read = await ReadAsync(after);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(items).RootElement, read), read.GetRawText());

        using var replace = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ("If-Match", $"\"{version}\""));
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        using var replaced = await client.GetAsync(new Uri(RunInputsUrl, UriKind.Relative), CancellationToken);
        Assert.Empty((await ReadAsync(replaced)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_run_inputs_save_with_a_stale_version_or_a_second_create_answers_409_and_a_bad_body_answers_400_and_the_file_keeps_its_bytes()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var file = Path.Combine(folder.Path, "run-inputs", "x.json");
        using var create = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[{"name":"a","operation":"echo","input":1}]}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var bytes = File.ReadAllBytes(file);

        using var stale = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ("If-Match", "\"stale\""));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var again = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using var other = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ("If-None-Match", "\"x\""));
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(bytes, File.ReadAllBytes(file));

        var ifMatch = ("If-Match", $"\"{Version(bytes)}\"");
        foreach (var body in new[]
        {
            """[]""",
            """{}""",
            """{"items":{}}""",
            """{"items":[],"extra":1}""",
            """{"items":[{"name":"","operation":"echo","input":1}]}""",
            """{"items":[{"name":"a","operation":"","input":1}]}""",
            """{"items":[{"name":"a","operation":"echo"}]}""",
            """{"items":[{"name":"a","operation":"echo","input":1,"extra":1}]}""",
            """{"items":[{"name":"a","operation":"echo","input":1},{"name":"a","operation":"echo","input":2}]}""",
            "not json",
        })
        {
            using var bad = await SendAsync(client, HttpMethod.Put, RunInputsUrl, body, ifMatch);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using var none = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""");
        Assert.Equal(HttpStatusCode.PreconditionRequired, none.StatusCode);
        using var text = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ifMatch, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        using var badPackage = await SendAsync(client, HttpMethod.Put, $"{Url}/packages/.hidden/run-inputs", """{"items":[]}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.BadRequest, badPackage.StatusCode);
        using var getBadPackage = await client.GetAsync(new Uri($"{Url}/packages/.hidden/run-inputs", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getBadPackage.StatusCode);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task Run_inputs_without_the_developer_role_the_author_role_or_the_switch_answer_403_on_a_read_and_a_write_and_create_no_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var on = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        await using var off = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: false);

        foreach (var (factory, identity) in new[] { (on, "author"), (on, "outsider"), (off, "developer") })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var put = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[]}""", ("If-None-Match", "*"));
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
            using var get = await client.GetAsync(new Uri(RunInputsUrl, UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
            Assert.False(File.Exists(Path.Combine(folder.Path, "run-inputs", "x.json")), identity);
        }
    }

    [Fact]
    public async Task A_run_inputs_save_is_a_revision_that_undo_reverts_and_it_changes_no_compile_hash_resource_or_package_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        var file = Path.Combine(folder.Path, "run-inputs", "x.json");
        var hash = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions, SampleModules.Modules).ContentHash;
        Assert.NotNull(hash);

        using var create = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[{"name":"a","operation":"echo","input":1}]}""", ("If-None-Match", "*"));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = File.ReadAllBytes(file);
        using var change = await SendAsync(client, HttpMethod.Put, RunInputsUrl, """{"items":[{"name":"b","operation":"echo","input":2}]}""", ("If-Match", create.Headers.ETag!.Tag));
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var compiled = ApplicationCompiler.Compile(folder.Path, SamplePackages.Versions, SampleModules.Modules);
        Assert.Equal(hash, compiled.ContentHash);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.File.StartsWith("run-inputs/", StringComparison.Ordinal));
        Assert.DoesNotContain(compiled.Resources, resource => resource.Path.StartsWith("run-inputs/", StringComparison.Ordinal));
        using var packageFiles = await client.GetAsync(new Uri($"{Url}/package-files", UriKind.Relative), CancellationToken);
        Assert.Empty((await ReadAsync(packageFiles)).GetProperty("items").EnumerateArray());

        using var repository = new Repository(Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git"));
        Assert.Equal(
            ["Start of history", "Create run inputs of package x", "Change run inputs of package x"],
            repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = "refs/heads/main", SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse })
                .Select(commit => commit.MessageShort));
        Assert.NotNull(repository.Branches["main"].Tip["run-inputs/x.json"]);

        using var list = await client.GetAsync(new Uri($"{Url}/revisions", UriKind.Relative), CancellationToken);
        var latest = (await ReadAsync(list)).GetProperty("items")[0];
        Assert.Equal("Change run inputs of package x", latest.GetProperty("summary").GetString());
        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{latest.GetProperty("id").GetString()}/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(created, File.ReadAllBytes(file));
    }

    [Fact]
    public async Task A_developer_moves_two_package_files_as_one_revision_and_one_undo_puts_both_back()
    {
        using var folder = StudioPublishTests.NewFolder();
        var seeded = SeedMoveFiles(folder.Path);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");

        using var move = await SendAsync(client, HttpMethod.Post, MoveUrl, MoveBody(("a.py", "lib/a.py", seeded["a.py"]), ("b.py", "lib/b.py", seeded["b.py"])));

        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await ReadAsync(move)).GetProperty("diagnostics").ValueKind);
        Assert.Equal(seeded["a.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "lib", "a.py"))));
        Assert.Equal(seeded["b.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "lib", "b.py"))));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "a.py")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "b.py")));

        using var revisionList = await client.GetAsync(new Uri($"{Url}/revisions", UriKind.Relative), CancellationToken);
        var latest = (await ReadAsync(revisionList)).GetProperty("items")[0];
        Assert.Equal("Move 2 package files in packages/x", latest.GetProperty("summary").GetString());
        using (var repository = new Repository(Path.Combine(revisions.Path, Path.GetFileName(folder.Path) + ".git")))
        {
            Assert.Equal(
                ["Start of history", "Move 2 package files in packages/x"],
                repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = "refs/heads/main", SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Reverse })
                    .Select(commit => commit.MessageShort));
        }

        using var undo = await SendAsync(client, HttpMethod.Post, $"{Url}/revisions/{latest.GetProperty("id").GetString()}/undo", null);
        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
        Assert.Equal(seeded["a.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "a.py"))));
        Assert.Equal(seeded["b.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "b.py"))));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "lib", "a.py")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "packages", "x", "lib", "b.py")));
    }

    [Fact]
    public async Task A_move_swaps_two_package_files_and_a_single_move_is_named_by_its_paths()
    {
        using var folder = StudioPublishTests.NewFolder();
        var seeded = SeedMoveFiles(folder.Path);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");

        using var swap = await SendAsync(client, HttpMethod.Post, MoveUrl, MoveBody(("a.py", "b.py", seeded["a.py"]), ("b.py", "a.py", seeded["b.py"])));
        Assert.Equal(HttpStatusCode.OK, swap.StatusCode);
        Assert.Equal(seeded["b.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "a.py"))));
        Assert.Equal(seeded["a.py"], Version(File.ReadAllBytes(Path.Combine(folder.Path, "packages", "x", "b.py"))));

        using var single = await SendAsync(client, HttpMethod.Post, MoveUrl, MoveBody(("a.py", "c.py", seeded["b.py"])));
        Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        using var revisionList = await client.GetAsync(new Uri($"{Url}/revisions", UriKind.Relative), CancellationToken);
        Assert.Equal(
            "Move package file packages/x/a.py to packages/x/c.py",
            (await ReadAsync(revisionList)).GetProperty("items")[0].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task A_move_with_a_stale_version_or_a_taken_path_answers_409_and_a_refused_path_or_body_answers_400_and_nothing_is_written()
    {
        using var folder = StudioPublishTests.NewFolder();
        var seeded = SeedMoveFiles(folder.Path);
        File.WriteAllText(Path.Combine(folder.Path, "packages", "x", "c.py"), "c = 3\n");
        File.WriteAllText(Path.Combine(folder.Path, "packages", "x", "dir", "d.py"), "d = 4\n");
        var before = Snapshot(folder.Path);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        using var client = factory.CreateClient().SignedInAs("developer");
        await CreateDraftAsync(factory, folder.Path, revisions.Path, "developer");
        var mainTip = MainTip(revisions.Path, folder.Path);

        var refusals = new (string Body, HttpStatusCode Status)[]
        {
            (MoveBody(("a.py", "lib/a.py", "stale")), HttpStatusCode.Conflict),
            (MoveBody(("missing.py", "lib/m.py", seeded["a.py"])), HttpStatusCode.Conflict),
            (MoveBody(("a.py", "c.py", seeded["a.py"])), HttpStatusCode.Conflict),
            (MoveBody(("a.py", "dir", seeded["a.py"])), HttpStatusCode.Conflict),
            (MoveBody(("a.py", "c.py/inside.py", seeded["a.py"])), HttpStatusCode.Conflict),
            (MoveBody(("a.py", "lib/a.py", seeded["a.py"]), ("b.py", "lib/b.py", "stale")), HttpStatusCode.Conflict),
            (MoveBody(("a.py", "../y.py", seeded["a.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("a.py", ".hidden", seeded["a.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("../y.py", "lib/a.py", seeded["a.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("a.py", "a.py", seeded["a.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("a.py", "lib/x.py", seeded["a.py"]), ("a.py", "lib/y.py", seeded["a.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("a.py", "lib/x.py", seeded["a.py"]), ("b.py", "lib/x.py", seeded["b.py"])), HttpStatusCode.BadRequest),
            (MoveBody(("a.py", "lib", seeded["a.py"]), ("b.py", "lib/b.py", seeded["b.py"])), HttpStatusCode.BadRequest),
            ("{}", HttpStatusCode.BadRequest),
            ("""{"moves":[]}""", HttpStatusCode.BadRequest),
            ("""{"moves":[{"from":"a.py","to":"lib/a.py"}]}""", HttpStatusCode.BadRequest),
            ("""{"moves":[{"from":"a.py","to":"lib/a.py","version":"x","extra":1}]}""", HttpStatusCode.BadRequest),
            ("""{"moves":[{"from":"a.py","to":"","version":"x"}]}""", HttpStatusCode.BadRequest),
            ("""{"moves":[{"from":"a.py","to":"lib/a.py","version":"x"}],"extra":1}""", HttpStatusCode.BadRequest),
        };
        foreach (var (body, status) in refusals)
        {
            using var response = await SendAsync(client, HttpMethod.Post, MoveUrl, body);
            Assert.True(status == response.StatusCode, $"{body} answered {response.StatusCode}.");
            Assert.Equal(before, Snapshot(folder.Path));
        }

        using var notJson = await SendAsync(client, HttpMethod.Post, MoveUrl, "x", contentType: "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notJson.StatusCode);
        using var badPackage = await SendAsync(client, HttpMethod.Post, $"{Url}/packages/.x/move", MoveBody(("a.py", "lib/a.py", seeded["a.py"])));
        Assert.Equal(HttpStatusCode.BadRequest, badPackage.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
        Assert.Equal(mainTip, MainTip(revisions.Path, folder.Path));
    }

    [Fact]
    public async Task A_move_without_the_developer_role_or_with_the_switch_off_answers_403_and_writes_nothing()
    {
        using var folder = StudioPublishTests.NewFolder();
        var seeded = SeedMoveFiles(folder.Path);
        var before = Snapshot(folder.Path);
        var body = MoveBody(("a.py", "lib/a.py", seeded["a.py"]));
        using var revisions = new TemporaryFolder();
        await using var on = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        await using var off = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: false);
        await CreateDraftAsync(on, folder.Path, revisions.Path, "developer");
        var mainTip = MainTip(revisions.Path, folder.Path);

        foreach (var (factory, identity) in new[] { (on, "author"), (on, "outsider"), (off, "developer") })
        {
            using var client = factory.CreateClient().SignedInAs(identity);
            using var response = await SendAsync(client, HttpMethod.Post, MoveUrl, body);
            Assert.True(HttpStatusCode.Forbidden == response.StatusCode, $"{identity} answered {response.StatusCode}.");
            Assert.Equal(before, Snapshot(folder.Path));
            Assert.Equal(mainTip, MainTip(revisions.Path, folder.Path));
        }
    }

    [Fact]
    public async Task A_developer_moves_package_files_in_a_draft_while_the_folder_and_main_stay_unchanged()
    {
        using var folder = StudioPublishTests.NewFolder();
        var seeded = SeedMoveFiles(folder.Path);
        var before = Snapshot(folder.Path);
        using var revisions = new TemporaryFolder();
        await using var factory = await CreateFactoryAsync(folder.Path, revisions.Path, switchOn: true);
        var draft = await CreateDraftAsync(factory, folder.Path, revisions.Path, "developer");
        var mainTip = MainTip(revisions.Path, folder.Path);
        using var client = factory.CreateClient().SignedInAs("developer");
        var draftUrl = $"{MoveUrl}?draft={draft}";

        using var stale = await SendAsync(client, HttpMethod.Post, draftUrl, MoveBody(("a.py", "lib/a.py", "stale")));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var missing = await SendAsync(client, HttpMethod.Post, draftUrl, MoveBody(("missing.py", "lib/m.py", seeded["a.py"])));
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        using var taken = await SendAsync(client, HttpMethod.Post, draftUrl, MoveBody(("a.py", "b.py", seeded["a.py"])));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        using var unknown = await SendAsync(client, HttpMethod.Post, $"{MoveUrl}?draft=nope", MoveBody(("a.py", "lib/a.py", seeded["a.py"])));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var move = await SendAsync(client, HttpMethod.Post, draftUrl, MoveBody(("a.py", "lib/a.py", seeded["a.py"]), ("b.py", "lib/b.py", seeded["b.py"])));
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await ReadAsync(move)).GetProperty("diagnostics").ValueKind);

        using var list = await client.GetAsync(new Uri($"{Url}/package-files?draft={draft}", UriKind.Relative), CancellationToken);
        Assert.Equal(
            ["dir/d.py", "lib/a.py", "lib/b.py"],
            (await ReadAsync(list)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
        Assert.Equal(before, Snapshot(folder.Path));
        Assert.Equal(mainTip, MainTip(revisions.Path, folder.Path));
        using var folderList = await client.GetAsync(new Uri($"{Url}/package-files", UriKind.Relative), CancellationToken);
        Assert.Equal(
            ["a.py", "b.py", "dir/d.py"],
            (await ReadAsync(folderList)).GetProperty("items").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
    }

    // Seeds packages/x/a.py, b.py and dir/d.py, and answers the version of a.py and b.py by path.
    private static Dictionary<string, string> SeedMoveFiles(string folder)
    {
        var directory = Path.Combine(folder, "packages", "x");
        Directory.CreateDirectory(Path.Combine(directory, "dir"));
        byte[] a = [.. "a = 1\n"u8];
        byte[] b = [.. "b = 2\n"u8];
        File.WriteAllBytes(Path.Combine(directory, "a.py"), a);
        File.WriteAllBytes(Path.Combine(directory, "b.py"), b);
        File.WriteAllBytes(Path.Combine(directory, "dir", "d.py"), [.. "d = 4\n"u8]);
        return new() { ["a.py"] = Version(a), ["b.py"] = Version(b) };
    }

    private static string MoveBody(params (string From, string To, string Version)[] moves) =>
        new JsonObject
        {
            ["moves"] = new JsonArray([.. moves.Select(move => (JsonNode)new JsonObject { ["from"] = move.From, ["to"] = move.To, ["version"] = move.Version })]),
        }.ToJsonString();

    // Every file under packages/ with the SHA-256 of its bytes, so a test sees any file that was added, removed or changed.
    private static IReadOnlyList<string> Snapshot(string folder) =>
        [.. Directory.EnumerateFiles(Path.Combine(folder, "packages"), "*", SearchOption.AllDirectories)
            .Select(file => $"{Path.GetRelativePath(folder, file)} {Version(File.ReadAllBytes(file))}")
            .Order(StringComparer.Ordinal)];

    private static async Task<string> CreateDraftAsync(WebApplicationFactory<Program> factory, string folder, string revisions, string userId)
    {
        var drafts = StudioDrafts.For(revisions, folder, userId, userId, factory.Services.GetRequiredService<IStudioWriteLock>())!;
        var recorder = StudioRevisions.For(revisions, folder, userId, userId, NullLogger<StudioRevisions>.Instance)!;
        return (await drafts.CreateAsync(recorder, CancellationToken)).Value!.Id;
    }

    private static string MainTip(string revisions, string folder)
    {
        using var repository = new Repository(Path.Combine(revisions, Path.GetFileName(folder) + ".git"));
        return repository.Refs["refs/heads/main"].TargetIdentifier;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return document.RootElement.Clone();
    }

    private static string Version(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? body,
        (string Name, string Value) header = default,
        string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue(contentType));
        }

        request.Headers.Add("X-Axis-Csrf", "1");
        if (header.Name is not null)
        {
            request.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder, string revisions, bool switchOn)
    {
        var platform = await database.CreateDatabaseAsync("studio_package_files", CancellationToken);
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
            builder.UseSetting("Studio:DeveloperRole", "developer");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("developer", "Developer User", "author", "developer"),
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("outsider", "Outsider User", "developer"),
                new TestIdentity("other", "Other User", "author", "developer"));
            if (switchOn)
            {
                // The configuration source has no switch, so the host turns it on for every tenant.
                builder.ConfigureTestServices(PackagesOnCatalog.Switch);
            }
        });
    }
}
