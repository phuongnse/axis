using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Server.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// The Studio release route answers what one stored release changes, which modules and packages it pins and which modules stop
/// it from replacing the live release.
/// </summary>
public sealed class StudioReleaseRouteTests : IAsyncLifetime
{
    private const string Host = "a.example.test";

    private const string ReleasesUrl = "/api/studio/apps/PurchaseRequests/releases";

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_live_release_compares_with_the_one_below_it_and_an_older_release_with_the_live_one()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_route", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);

        // Creating a client starts the server, which activates the folder as release 1.
        using var author = factory.CreateClient().SignedInAs("author");
        await EditTextAsync(folder.Path, "\"Departments\"", "\"Teams\"");
        await StudioReleaseHistoryTests.ActivateAsync(connectionString, folder.Path, ActivationActor.User("author", null, null, null));

        var live = await StudioReleaseHistoryTests.GetAsync(author, $"{ReleasesUrl}/2");

        Assert.Equal((2, 1, false, true), Header(live));
        var change = Assert.Single(live.GetProperty("changes").EnumerateArray());
        Assert.Equal(("texts/en.json", "changed"), (change.GetProperty("path").GetString(), change.GetProperty("change").GetString()));
        Assert.Equal(0, live.GetProperty("partChanges").GetArrayLength());
        Assert.Equal(0, live.GetProperty("blockers").GetArrayLength());
        var modules = live.GetProperty("modules").EnumerateArray().ToList();
        Assert.Equal(["department-budgets", "organization"], modules.Select(module => module.GetProperty("id").GetString()));
        Assert.All(modules, module =>
        {
            Assert.False(string.IsNullOrEmpty(module.GetProperty("label").GetString()));
            Assert.NotNull(module.GetProperty("version").GetString());
            Assert.Equal(module.GetProperty("version").GetString(), module.GetProperty("previousVersion").GetString());
        });
        var package = Assert.Single(live.GetProperty("packages").EnumerateArray());
        Assert.Equal("payment-references", package.GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(package.GetProperty("label").GetString()));
        Assert.Equal(package.GetProperty("version").GetString(), package.GetProperty("previousVersion").GetString());

        var older = await StudioReleaseHistoryTests.GetAsync(author, $"{ReleasesUrl}/1");

        Assert.Equal((1, 2, true, false), Header(older));
        var olderChange = Assert.Single(older.GetProperty("changes").EnumerateArray());
        Assert.Equal("texts/en.json", olderChange.GetProperty("path").GetString());
        Assert.Equal(0, older.GetProperty("partChanges").GetArrayLength());
        Assert.Equal(0, older.GetProperty("blockers").GetArrayLength());

        // The first release is live again, so nothing is below it to compare with.
        await EditTextAsync(folder.Path, "\"Teams\"", "\"Departments\"");
        await StudioReleaseHistoryTests.ActivateAsync(connectionString, folder.Path, ActivationActor.User("author", null, null, null));

        var first = await StudioReleaseHistoryTests.GetAsync(author, $"{ReleasesUrl}/1");

        Assert.Equal(JsonValueKind.Null, first.GetProperty("previous").ValueKind);
        Assert.False(first.GetProperty("previousIsLive").GetBoolean());
        Assert.True(first.GetProperty("live").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("changes").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("partChanges").ValueKind);
        Assert.Equal(2, first.GetProperty("modules").GetArrayLength());
        Assert.Equal(0, first.GetProperty("blockers").GetArrayLength());
        var second = await StudioReleaseHistoryTests.GetAsync(author, $"{ReleasesUrl}/2");
        Assert.Equal((2, 1, true, false), Header(second));
    }

    [Fact]
    public async Task A_release_that_lacks_a_module_of_the_live_release_names_it_as_a_blocker_and_a_part_change()
    {
        const string url = "/api/studio/apps/ModuleContractApp/releases";
        var fixtureModules = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");
        var fixtureApp = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleContractApp");
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(fixtureApp, folder.Path);
        var manifestPath = Path.Combine(folder.Path, "application.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath, CancellationToken))!.AsObject();
        manifest.Remove("modules");
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), CancellationToken);
        Directory.Delete(Path.Combine(folder.Path, "customizations"), recursive: true);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_route_blockers", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path, fixtureModules);

        // Creating a client starts the server, which activates the folder without the module as release 1.
        using var author = factory.CreateClient().SignedInAs("author");
        File.Copy(Path.Combine(fixtureApp, "application.json"), manifestPath, overwrite: true);
        Directory.CreateDirectory(Path.Combine(folder.Path, "customizations"));
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(fixtureApp, "customizations"), Path.Combine(folder.Path, "customizations"));
        await StudioReleaseHistoryTests.ActivateWithModulesAsync(connectionString, folder.Path, fixtureModules, ActivationActor.User("author", null, null, null));

        var older = await StudioReleaseHistoryTests.GetAsync(author, $"{url}/1");

        Assert.Equal((1, 2, true, false), Header(older));
        var blocker = Assert.Single(older.GetProperty("blockers").EnumerateArray());
        Assert.Equal(
            ("requests", "Requests", "1.0.0"),
            (blocker.GetProperty("module").GetString(), blocker.GetProperty("label").GetString(), blocker.GetProperty("liveVersion").GetString()));
        Assert.Equal(JsonValueKind.Null, blocker.GetProperty("version").ValueKind);
        var removed = Assert.Single(older.GetProperty("partChanges").EnumerateArray());
        Assert.Equal(
            ("module", "requests", "Requests", "removed", "1.0.0"),
            (removed.GetProperty("type").GetString(), removed.GetProperty("id").GetString(), removed.GetProperty("label").GetString(), removed.GetProperty("change").GetString(), removed.GetProperty("previousVersion").GetString()));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("version").ValueKind);
        Assert.Equal(0, older.GetProperty("modules").GetArrayLength());

        var live = await StudioReleaseHistoryTests.GetAsync(author, $"{url}/2");

        Assert.Equal((2, 1, false, true), Header(live));
        Assert.Equal(0, live.GetProperty("blockers").GetArrayLength());
        var added = Assert.Single(live.GetProperty("partChanges").EnumerateArray());
        Assert.Equal(
            ("module", "requests", "added", "1.0.0"),
            (added.GetProperty("type").GetString(), added.GetProperty("id").GetString(), added.GetProperty("change").GetString(), added.GetProperty("version").GetString()));
        Assert.Equal(JsonValueKind.Null, added.GetProperty("previousVersion").ValueKind);
        var module = Assert.Single(live.GetProperty("modules").EnumerateArray());
        Assert.Equal(("requests", "Requests", "1.0.0"), (module.GetProperty("id").GetString(), module.GetProperty("label").GetString(), module.GetProperty("version").GetString()));
        Assert.Equal(JsonValueKind.Null, module.GetProperty("previousVersion").ValueKind);
    }

    [Fact]
    public async Task A_user_without_the_author_role_is_refused_a_request_with_no_session_is_unauthorized_and_an_unknown_number_is_not_found()
    {
        using var folder = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), folder.Path);
        var connectionString = await _database.CreateDatabaseAsync("studio_release_route_refusals", CancellationToken);
        await using var factory = StudioReleaseHistoryTests.CreateFactory(connectionString, folder.Path);
        using var author = factory.CreateClient().SignedInAs("author");
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(requester, $"{ReleasesUrl}/1"));
        Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(anonymous, $"{ReleasesUrl}/1"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(author, $"{ReleasesUrl}/1"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(author, $"{ReleasesUrl}/99"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(author, $"{ReleasesUrl}/abc"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(author, $"{ReleasesUrl}/-1"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(author, "/api/studio/apps/Unknown/releases/1"));
    }

    private static (int Number, int? Previous, bool PreviousIsLive, bool Live) Header(JsonElement release) =>
        (
            release.GetProperty("number").GetInt32(),
            release.GetProperty("previous").ValueKind == JsonValueKind.Null ? null : release.GetProperty("previous").GetInt32(),
            release.GetProperty("previousIsLive").GetBoolean(),
            release.GetProperty("live").GetBoolean());

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string url)
    {
        using var request = RecordApiFixture.Request(url, Host);
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    private static async Task EditTextAsync(string folder, string from, string to)
    {
        var path = Path.Combine(folder, "texts", "en.json");
        var text = await File.ReadAllTextAsync(path, CancellationToken);
        Assert.Contains(from, text, StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, text.Replace(from, to, StringComparison.Ordinal), CancellationToken);
    }
}
