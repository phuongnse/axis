using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// The package file routes of the authoring API, see <c>docs/reference/studio-packages.md#the-authoring-api</c>. A write takes the
/// write lock in the platform database, so these tests run on real PostgreSQL. The identity <c>developer</c> holds the author and
/// developer roles, <c>author</c> holds the author role only and <c>outsider</c> holds the developer role only. The tenant's
/// application packages switch is on in the host unless a test turns it off, see <see cref="CreateFactoryAsync"/>.
/// </summary>
public sealed class StudioPackageFileTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests";

    private const string FileUrl = $"{Url}/packages/x/files/adapter.py";

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
