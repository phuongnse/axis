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
    }

    private static string? ActorName(JsonElement history, string actor) =>
        history.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("actor").GetString() == actor).GetProperty("actorName").GetString();

    // The history is append-only but accepts new rows. This one stands for a row written before M27, which kept the name.
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

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
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
    private static async Task ActivateWithModulesAsync(string connectionString, string folder, string modulesPath, ActivationActor actor)
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

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, string folder, string? modulesPath = null) =>
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
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"), new TestIdentity("requester", "Requester", "employee"));
        });

    private static void CopyDirectory(string source, string target)
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
