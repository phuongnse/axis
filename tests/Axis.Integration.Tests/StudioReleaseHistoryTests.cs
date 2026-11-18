using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Profiles;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>The Studio release history route answers what the tenant database holds, as the activations wrote it.</summary>
public sealed class StudioReleaseHistoryTests : IAsyncLifetime
{
    private const string Tenant = "a";

    private const string Host = "a.example.test";

    private const string ReleasesUrl = "/api/studio/apps/PurchaseRequests/releases";

    private const string ReleaseListUrl = "/api/studio/apps/PurchaseRequests/release-list";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_route_answers_the_activations_newest_first_and_names_the_active_release()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_releases", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);

        // Creating a client starts the server, which migrates the database and activates the folder first.
        using var author = factory.CreateClient().SignedInAs("author");
        var startup = await GetAsync(author, ReleasesUrl);
        var startupItem = Assert.Single(startup.GetProperty("items").EnumerateArray());
        Assert.Equal(("startup", 1), (startupItem.GetProperty("actor").GetString(), startupItem.GetProperty("number").GetInt32()));
        Assert.Equal(JsonValueKind.Null, startupItem.GetProperty("requestId").ValueKind);
        Assert.Equal(startupItem.GetProperty("releaseId").GetGuid(), startup.GetProperty("active").GetProperty("releaseId").GetGuid());
        Assert.Equal(startupItem.GetProperty("contentHash").GetString(), startup.GetProperty("folderContentHash").GetString());

        // A folder identical to the live release has no changes.
        Assert.Equal(JsonValueKind.Array, startup.GetProperty("changes").ValueKind);
        Assert.Equal(0, startup.GetProperty("changes").GetArrayLength());
        Assert.Equal(JsonValueKind.Array, startup.GetProperty("partChanges").ValueKind);
        Assert.Equal(0, startup.GetProperty("partChanges").GetArrayLength());

        await EditTextAsync(folder.Path, "\"Departments\"", "\"Teams\"");
        var requestId = Guid.NewGuid();
        await ActivateAsync(connectionString, folder.Path, ActivationActor.User("author", "Spring", "Teams rename", requestId));
        await UpsertProfileAsync(connectionString, "author", "Author User");

        var history = await GetAsync(author, ReleasesUrl);

        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal([2, 1], items.Select(item => item.GetProperty("number").GetInt32()));
        var newest = items[0];
        Assert.Equal(
            ("author", "Author User", "Spring", "Teams rename", requestId),
            (newest.GetProperty("actor").GetString(), newest.GetProperty("actorName").GetString(), newest.GetProperty("label").GetString(), newest.GetProperty("note").GetString(), newest.GetProperty("requestId").GetGuid()));
        Assert.True(newest.GetProperty("activatedAt").GetDateTimeOffset() >= items[1].GetProperty("activatedAt").GetDateTimeOffset());
        var active = history.GetProperty("active");
        Assert.Equal(newest.GetProperty("releaseId").GetGuid(), active.GetProperty("releaseId").GetGuid());
        Assert.Equal(2, active.GetProperty("number").GetInt32());
        Assert.Equal(newest.GetProperty("contentHash").GetString(), active.GetProperty("contentHash").GetString());
        Assert.Equal(newest.GetProperty("contentHash").GetString(), history.GetProperty("folderContentHash").GetString());

        // A folder edit that is not activated differs from the active release.
        await EditTextAsync(folder.Path, "\"Teams\"", "\"Groups\"");
        var edited = await GetAsync(author, ReleasesUrl);
        Assert.NotEqual(newest.GetProperty("contentHash").GetString(), edited.GetProperty("folderContentHash").GetString());
        Assert.Equal(2, edited.GetProperty("items").GetArrayLength());

        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task The_release_list_answers_one_row_for_each_release_and_pages_sorts_searches_and_filters_them()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_list", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        await ActivateThreeReleasesAsync(connectionString, folder.Path);

        // Release 1 was activated again, so it has two activations but one row, which takes its first activation.
        var all = await GetAsync(author, ReleaseListUrl);
        Assert.Equal([3, 2, 1], Numbers(all));
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
        Assert.Equal((1, 20), (all.GetProperty("page").GetInt32(), all.GetProperty("pageSize").GetInt32()));
        var rows = all.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["startup", "author", "author"], rows.Reverse<JsonElement>().Select(row => row.GetProperty("actor").GetString()));
        Assert.Equal([true, false, false], rows.Select(row => row.GetProperty("live").GetBoolean()));
        Assert.Equal([false, false, false], rows.Select(row => row.GetProperty("offStandard").GetBoolean()));
        Assert.Equal(("Autumn", "Spring"), (rows[0].GetProperty("label").GetString(), rows[1].GetProperty("label").GetString()));
        Assert.Equal("Teams rename", rows[1].GetProperty("note").GetString());
        Assert.Equal(JsonValueKind.Null, rows[2].GetProperty("label").ValueKind);

        var second = await GetAsync(author, $"{ReleaseListUrl}?pageSize=2&page=2");
        Assert.Equal([1], Numbers(second));
        Assert.Equal(3, second.GetProperty("totalCount").GetInt64());
        Assert.Equal([1, 2, 3], Numbers(await GetAsync(author, $"{ReleaseListUrl}?sort=number")));
        Assert.Equal([3, 2, 1], Numbers(await GetAsync(author, $"{ReleaseListUrl}?sort=label")));
        Assert.Equal([1, 2, 3], Numbers(await GetAsync(author, $"{ReleaseListUrl}?sort=-label")));

        var searched = await GetAsync(author, $"{ReleaseListUrl}?search=spring");
        Assert.Equal([2], Numbers(searched));
        Assert.Equal(1, searched.GetProperty("totalCount").GetInt64());
        Assert.Equal([2], Numbers(await GetAsync(author, $"{ReleaseListUrl}?search=rename")));

        var filtered = await GetAsync(author, $"{ReleaseListUrl}?filter={Uri.EscapeDataString("""{"all":[{"field":"number","op":"gte","value":2}]}""")}");
        Assert.Equal([3, 2], Numbers(filtered));
        Assert.Equal(2, filtered.GetProperty("totalCount").GetInt64());

        using var undeclared = await author.SendAsync(
            RecordApiFixture.Request($"{ReleaseListUrl}?filter={Uri.EscapeDataString("""{"all":[{"field":"contentHash","op":"eq","value":"x"}]}""")}", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, undeclared.StatusCode);
        Assert.Contains("filter", await ErrorNamesAsync(undeclared));
        using var unsorted = await author.SendAsync(RecordApiFixture.Request($"{ReleaseListUrl}?sort=live", Host), CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, unsorted.StatusCode);
        Assert.Contains("sort", await ErrorNamesAsync(unsorted));

        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(ReleaseListUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(ReleaseListUrl, Host), CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task The_release_history_ignores_the_list_parameters_and_answers_every_activation_newest_first()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_history_params", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        await ActivateThreeReleasesAsync(connectionString, folder.Path);

        var plain = await GetAsync(author, ReleasesUrl);
        var withParameters = await GetAsync(author, $"{ReleasesUrl}?page=2&pageSize=1&sort=label&search=x");

        // Release 1 is in the history twice, and its second activation is the newest.
        Assert.Equal([1, 3, 2, 1], Numbers(withParameters));
        Assert.Equal(plain.GetProperty("items").GetRawText(), withParameters.GetProperty("items").GetRawText());
        Assert.Equal(plain.GetProperty("active").GetRawText(), withParameters.GetProperty("active").GetRawText());
        Assert.False(withParameters.TryGetProperty("totalCount", out _));
    }

    // Release 1 comes from startup. Releases 2 and 3 are labelled, and release 1 is activated again a day later.
    private static async Task ActivateThreeReleasesAsync(string connectionString, string folder)
    {
        await EditTextAsync(folder, "\"Departments\"", "\"Teams\"");
        await ActivateAsync(connectionString, folder, ActivationActor.User("author", "Spring", "Teams rename", Guid.NewGuid()));
        await EditTextAsync(folder, "\"Teams\"", "\"Groups\"");
        await ActivateAsync(connectionString, folder, ActivationActor.User("author", "Autumn", null, null));
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            """
            INSERT INTO axis.release_activations (id, release_id, application_id, number, content_hash, activated_at, actor)
            SELECT uuidv7(), release_id, application_id, number, content_hash, activated_at + interval '1 day', 'author'
            FROM axis.release_activations WHERE actor = 'startup'
            """,
            CancellationToken);
    }

    private static List<int> Numbers(JsonElement answer) =>
        [.. answer.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("number").GetInt32())];

    private static async Task<List<string>> ErrorNamesAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    [Fact]
    public async Task The_changes_name_the_files_added_changed_and_removed_since_the_live_release_and_the_removed_role_keeps_its_labels()
    {
        using var folder = new TemporaryFolder();
        var sample = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        CopyDirectory(sample, folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_changes", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        await File.WriteAllTextAsync(
            Path.Combine(folder.Path, "roles", "auditor.json"),
            $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "auditor", "formatVersion": 1, "label": { "textKey": "role.operator" } }""",
            CancellationToken);
        await EditTextAsync(folder.Path, "\"Departments\"", "\"Teams\"");
        File.Delete(Path.Combine(folder.Path, "roles", "operator.json"));

        var history = await GetAsync(author, ReleasesUrl);

        // The policy still names the removed role, so the folder has errors, and its changes are still listed.
        Assert.Equal(JsonValueKind.Null, history.GetProperty("folderContentHash").ValueKind);
        var changes = history.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(
            [("roles/auditor.json", "added"), ("roles/operator.json", "removed"), ("texts/en.json", "changed")],
            changes.Select(change => (change.GetProperty("path").GetString()!, change.GetProperty("change").GetString()!)));
        var removed = changes[1];
        Assert.Equal(("role", "operator"), (removed.GetProperty("kind").GetString(), removed.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("locale").ValueKind);
        var titles = removed.GetProperty("titles");
        using var vietnamese = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(sample, "texts", "vi.json"), CancellationToken));
        Assert.Equal(
            ("Operator", vietnamese.RootElement.GetProperty("texts").GetProperty("role.operator").GetString()),
            (titles.GetProperty("en").GetString(), titles.GetProperty("vi").GetString()));
        foreach (var change in new[] { changes[0], changes[2] })
        {
            Assert.All(["kind", "name", "titles", "locale"], name => Assert.Equal(JsonValueKind.Null, change.GetProperty(name).ValueKind));
        }

        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task A_server_with_no_live_release_answers_no_active_release_and_no_changes()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_none", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);

        // Creating a client starts the server, which activates the folder. Removing the active row leaves no live release.
        using var author = factory.CreateClient().SignedInAs("author");
        await PostgreSqlServer.AdminScalarAsync(connectionString, "DELETE FROM axis.active_releases", CancellationToken);

        var history = await GetAsync(author, ReleasesUrl);

        Assert.Equal(JsonValueKind.Null, history.GetProperty("active").ValueKind);
        Assert.Equal(JsonValueKind.Null, history.GetProperty("changes").ValueKind);
        Assert.Equal(JsonValueKind.Null, history.GetProperty("partChanges").ValueKind);
    }

    [Fact]
    public async Task A_module_the_folder_pins_and_the_live_release_lacks_is_an_added_part_change()
    {
        const string url = "/api/studio/apps/ModuleContractApp/releases";
        var fixtureModules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");
        var fixtureApp = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp");
        using var folder = new TemporaryFolder();
        CopyDirectory(fixtureApp, folder.Path);
        var manifestPath = Path.Combine(folder.Path, "application.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, CancellationToken))!.AsObject();
        manifest.Remove("modules");
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), CancellationToken);
        Directory.Delete(Path.Combine(folder.Path, "customizations"), recursive: true);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_history_added", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path, fixtureModules);

        // Creating a client starts the server, which activates the folder without the module as release 1. The folder then
        // pins the module again, and it is not activated.
        using var author = factory.CreateClient().SignedInAs("author");
        File.Copy(Path.Combine(fixtureApp, "application.json"), manifestPath, overwrite: true);
        Directory.CreateDirectory(Path.Combine(folder.Path, "customizations"));
        CopyDirectory(Path.Combine(fixtureApp, "customizations"), Path.Combine(folder.Path, "customizations"));

        var history = await GetAsync(author, url);

        Assert.NotEqual(JsonValueKind.Null, history.GetProperty("folderContentHash").ValueKind);
        var added = Assert.Single(history.GetProperty("partChanges").EnumerateArray());
        Assert.Equal(
            ("module", "requests", "Requests", "added", "1.0.0"),
            (added.GetProperty("type").GetString(), added.GetProperty("id").GetString(), added.GetProperty("label").GetString(), added.GetProperty("change").GetString(), added.GetProperty("version").GetString()));
        Assert.Equal(JsonValueKind.Null, added.GetProperty("previousVersion").ValueKind);
    }

    [Fact]
    public async Task A_module_and_a_package_the_folder_drops_are_removed_part_changes_even_with_errors()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_history_removed", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        var manifestPath = Path.Combine(folder.Path, "application.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, CancellationToken))!;
        RemoveEntry(manifest["modules"]!, "department-budgets");
        RemoveEntry(manifest["packages"]!, "payment-terms");
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), CancellationToken);

        var history = await GetAsync(author, ReleasesUrl);

        // The folder has errors, so it has no content hash, and its removed parts are still listed, modules first.
        Assert.Equal(JsonValueKind.Null, history.GetProperty("folderContentHash").ValueKind);
        var parts = history.GetProperty("partChanges").EnumerateArray().ToList();
        Assert.Equal(
            [("module", "department-budgets", "removed"), ("package", "payment-terms", "removed")],
            parts.Select(part => (part.GetProperty("type").GetString()!, part.GetProperty("id").GetString()!, part.GetProperty("change").GetString()!)));
        var module = parts[0];
        Assert.False(string.IsNullOrEmpty(module.GetProperty("label").GetString()));
        Assert.False(string.IsNullOrEmpty(module.GetProperty("previousVersion").GetString()));
        Assert.Equal(JsonValueKind.Null, module.GetProperty("version").ValueKind);
        var package = parts[1];
        Assert.Equal(("Payment terms", "1.0.0"), (package.GetProperty("label").GetString(), package.GetProperty("previousVersion").GetString()));
        Assert.Equal(JsonValueKind.Null, package.GetProperty("version").ValueKind);

        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
    }

    private static void RemoveEntry(JsonNode entries, string id)
    {
        var array = entries.AsArray();
        var entry = Assert.Single(array, node => node!["id"]!.GetValue<string>() == id);
        array.Remove(entry);
    }

    [Fact]
    public async Task An_activations_actor_name_is_the_one_in_the_profile_else_the_stored_one_of_an_older_row_else_null()
    {
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_names", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        await EditTextAsync(folder.Path, "\"Departments\"", "\"Teams\"");
        await ActivateAsync(connectionString, folder.Path, ActivationActor.User("author", null, null, null));
        await EditTextAsync(folder.Path, "\"Teams\"", "\"Groups\"");
        await ActivateAsync(connectionString, folder.Path, ActivationActor.User("gone", null, null, null));
        await StoreOlderRowAsync(connectionString, "legacy", "Legacy User");
        await UpsertProfileAsync(connectionString, "author", "Renamed Author");
        await UpsertProfileAsync(connectionString, "legacy", "Renamed Legacy");
        await UpsertProfileAsync(connectionString, "gone", "Gone User");
        await UpsertProfileAsync(connectionString, "startup", "Mallory");

        var renamed = await GetAsync(author, ReleasesUrl);

        Assert.Equal("Renamed Author", ActorName(renamed, "author"));
        Assert.Equal("Renamed Legacy", ActorName(renamed, "legacy"));
        Assert.Equal("Gone User", ActorName(renamed, "gone"));
        Assert.Null(ActorName(renamed, "startup"));

        await PostgreSqlServer.AdminScalarAsync(connectionString, "DELETE FROM axis.user_profiles WHERE user_id IN ('author', 'legacy', 'gone')", CancellationToken);
        var deleted = await GetAsync(author, ReleasesUrl);

        Assert.Null(ActorName(deleted, "author"));
        Assert.Equal("Legacy User", ActorName(deleted, "legacy"));
        Assert.Null(ActorName(deleted, "gone"));
        Assert.Contains(deleted.GetProperty("items").EnumerateArray(), item => item.GetProperty("actor").GetString() == "gone");
    }

    [Fact]
    public async Task A_release_activated_with_a_relax_entry_is_off_standard_and_lists_the_relaxed_point_and_its_reason()
    {
        const string point = "Request/transitions/submit";
        const string reason = "Tests skip it.";
        var fixtureModules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");
        using var folder = new TemporaryFolder();
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_relax", CancellationToken);
        await using var factory = CreateFactory(connectionString, folder.Path, fixtureModules);

        // Creating a client starts the server, which activates the folder without relax entries as release 1.
        using var author = factory.CreateClient().SignedInAs("author");
        var manifestPath = Path.Combine(folder.Path, "application.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, CancellationToken))!;
        manifest["modules"]![0]!["relax"] = new JsonArray(new JsonObject { ["point"] = point, ["reason"] = reason });
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), CancellationToken);
        await ActivateWithModulesAsync(connectionString, folder.Path, fixtureModules, ActivationActor.User("author", null, null, null));

        var history = await GetAsync(author, "/api/studio/apps/ModuleContractApp/releases");

        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([2, 1], items.Select(item => item.GetProperty("number").GetInt32()));
        Assert.True(items[0].GetProperty("offStandard").GetBoolean());
        var relaxed = Assert.Single(items[0].GetProperty("relaxed").EnumerateArray());
        Assert.Equal(
            ("requests", point, reason),
            (relaxed.GetProperty("module").GetString(), relaxed.GetProperty("point").GetString(), relaxed.GetProperty("reason").GetString()));
        Assert.False(items[1].GetProperty("offStandard").GetBoolean());
        Assert.Equal(0, items[1].GetProperty("relaxed").GetArrayLength());

        // The words come from the module copy that the release stores. The fixture entity has no label, so its name stands in.
        Assert.Equal(("Requests", "transition"), (relaxed.GetProperty("moduleTitle").GetString(), relaxed.GetProperty("part").GetString()));
        Assert.Equal("Submit", relaxed.GetProperty("titles").GetProperty("en").GetString());
        Assert.Equal("Request", relaxed.GetProperty("entityTitles").GetProperty("en").GetString());
    }

    private static string? ActorName(JsonElement history, string actor) =>
        history.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("actor").GetString() == actor).GetProperty("actorName").GetString();

    // The history is append-only but accepts new rows. This one stands for a row written before the history stopped storing the name, when rows kept it.
    private static async Task StoreOlderRowAsync(string connectionString, string actor, string name)
    {
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"""
            INSERT INTO axis.release_activations (id, release_id, application_id, number, content_hash, activated_at, actor, actor_name)
            SELECT uuidv7(), release_id, application_id, number, content_hash, activated_at, '{actor}', '{name}'
            FROM axis.release_activations WHERE actor = 'startup'
            """,
            CancellationToken);
    }

    private static async Task UpsertProfileAsync(string connectionString, string userId, string displayName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await UserProfiles.UpsertAsync(connection, userId, displayName, null, null, CancellationToken);
    }

    private static async Task EditTextAsync(string folder, string from, string to)
    {
        var path = Path.Combine(folder, "texts", "en.json");
        var text = await File.ReadAllTextAsync(path, CancellationToken);
        Assert.Contains(from, text, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text.Replace(from, to, StringComparison.Ordinal), CancellationToken);
    }

    internal static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        using var request = RecordApiFixture.Request(url, Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.Clone();
    }

    internal static async Task ActivateAsync(string connectionString, string folder, ActivationActor actor)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, SamplePackages.Versions, SampleModules.Modules, CancellationToken);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Severity == Axis.Configuration.Diagnostics.DiagnosticSeverity.Error);
        var deploymentBindings = new Dictionary<string, ActivationBinding>
        {
            ["PurchasingSystemUrl"] = new(ActivationBinding.UrlKind, new Uri("https://purchasing.example.test")),
        };
        PurchasingBindings.AddTo(deploymentBindings);
        CompanyBindings.AddTo(deploymentBindings);
        var bindings = new ActivationBindings(Tenant, deploymentBindings, AllowHttp: false);
        var result = await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance,
            ReleaseActivator.DefaultInlineRecomputeLimit, actor, CancellationToken);
        Assert.Empty(result.Diagnostics);
    }

    // Like ActivateAsync, but for an application that pins modules from modulesPath and needs no bindings.
    internal static async Task ActivateWithModulesAsync(string connectionString, string folder, string modulesPath, ActivationActor actor)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, SamplePackages.Versions, ModuleCatalog.Load(modulesPath).Modules, CancellationToken);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Severity == Axis.Configuration.Diagnostics.DiagnosticSeverity.Error);
        var bindings = new ActivationBindings(Tenant, new Dictionary<string, ActivationBinding>(), AllowHttp: false);
        var result = await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance,
            ReleaseActivator.DefaultInlineRecomputeLimit, actor, CancellationToken);
        Assert.Empty(result.Diagnostics);
    }

    internal static WebApplicationFactory<Program> CreateFactory(string connectionString, string folder, string? modulesPath = null, string? revisionsPath = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Kind", "url");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
            foreach (var (key, value) in PurchasingBindings.Settings(Tenant))
            {
                builder.UseSetting(key, value);
            }
            foreach (var (key, value) in CompanyBindings.Settings(Tenant))
            {
                builder.UseSetting(key, value);
            }
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", modulesPath ?? SampleModules.Path);
            builder.UseSetting("ActivateOnStartup:0", folder);
            builder.UseSetting("Studio:Folders:0", folder);
            if (revisionsPath is not null)
            {
                builder.UseSetting("Studio:RevisionsPath", revisionsPath);
            }

            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"), new TestIdentity("requester", "Requester", "employee"));
        });

    internal static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }
}
