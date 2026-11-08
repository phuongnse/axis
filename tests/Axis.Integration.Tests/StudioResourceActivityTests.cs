using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The resource activity route answers each resource's status against the live release, and its last writer while the file
/// still has the version that the write left, as <c>docs/reference/studio-resource-form.md#resource-activity</c> says.
/// </summary>
public sealed class StudioResourceActivityTests : IAsyncLifetime
{
    private const string Host = "a.example.test";

    private const string ActivityUrl = "/api/studio/apps/PurchaseRequests/resource-activity";

    private const string ModuleActivityUrl = "/api/studio/apps/ModuleContractApp/resource-activity";

    private const string RequestEntityId = "4d2a6c81-3e5b-4f70-8a1c-9b0d2e4f6a01";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task A_resource_is_published_changed_or_new_against_the_live_release_and_a_released_file_that_no_longer_loads_is_changed()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_status", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");

        var published = await GetAsync(author, ActivityUrl);

        var items = published.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("module").ValueKind == JsonValueKind.String);
        Assert.All(items, item => Assert.Contains(item.GetProperty("status").GetString(), new string?[] { "published", null }));
        Assert.Contains(items, item => item.GetProperty("status").GetString() == "published");

        // A released file that is edited, a created file, and a released file that is no longer JSON.
        var texts = Path.Combine(folder.Path, "texts", "en.json");
        var textId = ResourceId(texts);
        await File.WriteAllTextAsync(texts, (await File.ReadAllTextAsync(texts, CancellationToken)).Replace("\"Departments\"", "\"Teams\"", StringComparison.Ordinal), CancellationToken);
        var brokenPath = Path.Combine(folder.Path, "roles", "operator.json");
        var brokenId = ResourceId(brokenPath);
        await File.WriteAllTextAsync(brokenPath, "{", CancellationToken);
        var createdId = Guid.NewGuid();
        using (var create = RecordApiFixture.Request(
            HttpMethod.Post,
            "/api/studio/apps/PurchaseRequests/resources",
            Host,
            $$"""{ "id": "{{createdId}}", "kind": "role", "name": "auditor", "formatVersion": 1, "label": { "textKey": "role.operator" } }"""))
        using (var response = await author.SendAsync(create, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var edited = await GetAsync(author, ActivityUrl);

        Assert.Equal("changed", StatusOf(edited, textId));
        Assert.Equal("changed", StatusOf(edited, brokenId));
        Assert.Equal("new", StatusOf(edited, createdId.ToString("D")));
        var untouched = edited.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("module").ValueKind == JsonValueKind.Null)
            .Select(item => item.GetProperty("id").GetString())
            .Except([textId, brokenId, createdId.ToString("D")]);
        Assert.All(untouched, id => Assert.NotEqual("new", StatusOf(edited, id!)));
    }

    [Fact]
    public async Task With_no_live_release_every_resource_is_new_and_has_no_writer()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_none", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);

        // Creating a client starts the server, which migrates the database and activates the folder. Removing the active row leaves no live release.
        using var author = factory.CreateClient().SignedInAs("author");
        await PostgreSqlServer.AdminScalarAsync(connectionString, "DELETE FROM axis.active_releases", CancellationToken);

        var activity = await GetAsync(author, ActivityUrl);

        var items = activity.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, item => item.GetProperty("module").ValueKind == JsonValueKind.String);
        Assert.All(items.Where(item => item.GetProperty("status").ValueKind != JsonValueKind.Null), item => Assert.Equal("new", item.GetProperty("status").GetString()));
        Assert.All(items, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("modifiedBy").ValueKind));
    }

    [Fact]
    public async Task A_patch_answers_its_writer_until_the_file_is_edited_on_disk()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_writer", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        var texts = Path.Combine(folder.Path, "texts", "en.json");
        var textId = ResourceId(texts);
        var before = await GetAsync(author, ActivityUrl);
        Assert.Equal(JsonValueKind.Null, Item(before, textId).GetProperty("modifiedBy").ValueKind);

        var version = (await GetAsync(author, $"/api/studio/apps/PurchaseRequests/resources/{textId}")).GetProperty("version").GetString();
        using (var patch = RecordApiFixture.Request(
            HttpMethod.Patch,
            $"/api/studio/apps/PurchaseRequests/resources/{textId}",
            Host,
            """[{ "op": "add", "path": "/texts/activity.test", "value": "One" }]""",
            "application/json-patch+json"))
        {
            patch.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
            using var response = await author.SendAsync(patch, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var written = Item(await GetAsync(author, ActivityUrl), textId);

        Assert.Equal("changed", written.GetProperty("status").GetString());
        Assert.Equal("Author User", written.GetProperty("modifiedBy").GetString());
        Assert.NotEqual(default, written.GetProperty("modifiedAt").GetDateTimeOffset());

        await File.WriteAllTextAsync(texts, (await File.ReadAllTextAsync(texts, CancellationToken)).Replace("\"One\"", "\"Two\"", StringComparison.Ordinal), CancellationToken);

        var edited = Item(await GetAsync(author, ActivityUrl), textId);

        Assert.Equal("changed", edited.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("modifiedBy").ValueKind);
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("modifiedAt").ValueKind);
    }

    [Fact]
    public async Task A_module_layer_save_names_its_writer_on_the_module_resource()
    {
        var modules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_layer", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, modules);
        using var author = factory.CreateClient().SignedInAs("author");
        var before = ModuleItem(await GetAsync(author, ModuleActivityUrl), "requests", RequestEntityId);
        Assert.Equal("published", before.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("modifiedBy").ValueKind);

        var url = $"/api/studio/apps/ModuleContractApp/modules/requests/resources/{RequestEntityId}";
        var module = await GetAsync(author, url);
        var content = JsonNode.Parse(module.GetProperty("content").GetRawText())!;
        ((JsonArray)content["fields"]![2]!["values"]!).Add("rejected");
        using (var put = RecordApiFixture.Request(HttpMethod.Put, url, Host, content.ToJsonString()))
        {
            put.Headers.TryAddWithoutValidation("If-Match", $"\"{module.GetProperty("layer").GetProperty("version").GetString()}\"");
            using var response = await author.SendAsync(put, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var saved = ModuleItem(await GetAsync(author, ModuleActivityUrl), "requests", RequestEntityId);

        Assert.Equal("changed", saved.GetProperty("status").GetString());
        Assert.Equal("Author User", saved.GetProperty("modifiedBy").GetString());
        Assert.NotEqual(default, saved.GetProperty("modifiedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_module_resource_has_its_titles_and_descriptions_from_the_modules_texts()
    {
        using var modules = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules"), modules.Path);
        var version = Path.Combine(modules.Path, "requests", "1.0.0");
        var entityPath = Path.Combine(version, "entities", "request.json");
        var entity = JsonNode.Parse(await File.ReadAllTextAsync(entityPath, CancellationToken))!;
        entity["label"] = new JsonObject { ["textKey"] = "requests.request.label" };
        entity["description"] = new JsonObject { ["textKey"] = "requests.request.description" };
        await File.WriteAllTextAsync(entityPath, entity.ToJsonString(), CancellationToken);
        var textsPath = Path.Combine(version, "texts", "en.json");
        var texts = JsonNode.Parse(await File.ReadAllTextAsync(textsPath, CancellationToken))!;
        texts["texts"]!["requests.request.label"] = "Request";
        texts["texts"]!["requests.request.description"] = "A request for approval.";
        await File.WriteAllTextAsync(textsPath, texts.ToJsonString(), CancellationToken);
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_titles", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, modules.Path);
        using var author = factory.CreateClient().SignedInAs("author");

        var item = ModuleItem(await GetAsync(author, ModuleActivityUrl), "requests", RequestEntityId);

        Assert.Equal("Request", item.GetProperty("titles").GetProperty("en").GetString());
        Assert.Equal("A request for approval.", item.GetProperty("descriptions").GetProperty("en").GetString());
        var folderItem = Item(await GetAsync(author, ModuleActivityUrl), ResourceId(Path.Combine(folder.Path, "texts", "en.json")));
        Assert.Equal(JsonValueKind.Null, folderItem.GetProperty("titles").ValueKind);
        Assert.Equal(JsonValueKind.Null, folderItem.GetProperty("descriptions").ValueKind);
    }

    [Fact]
    public async Task A_user_without_the_author_role_gets_403_and_a_request_without_a_session_gets_401()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_activity_denied", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(ActivityUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(ActivityUrl, Host), CancellationToken)).StatusCode);
    }

    private static string ResourceId(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return document.RootElement.GetProperty("id").GetString()!;
    }

    private static JsonElement Item(JsonElement activity, string id) =>
        activity.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id && item.GetProperty("module").ValueKind == JsonValueKind.Null);

    private static JsonElement ModuleItem(JsonElement activity, string module, string id) =>
        activity.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id && item.GetProperty("module").GetString() == module);

    private static string? StatusOf(JsonElement activity, string id) => Item(activity, id).GetProperty("status").GetString();

    private static Task<JsonElement> GetAsync(HttpClient client, string url) => StudioReleaseHistoryTests.GetAsync(client, url);
}
