using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
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
        using var author = factory.CreateClient().SignedInAs("hoa");
        var startup = await GetAsync(author, ReleasesUrl);
        var startupItem = Assert.Single(startup.GetProperty("items").EnumerateArray());
        Assert.Equal(("startup", 1), (startupItem.GetProperty("actor").GetString(), startupItem.GetProperty("number").GetInt32()));
        Assert.Equal(JsonValueKind.Null, startupItem.GetProperty("requestId").ValueKind);
        Assert.Equal(startupItem.GetProperty("releaseId").GetGuid(), startup.GetProperty("active").GetProperty("releaseId").GetGuid());
        Assert.Equal(startupItem.GetProperty("contentHash").GetString(), startup.GetProperty("folderContentHash").GetString());

        await EditTextAsync(folder.Path, "\"Departments\"", "\"Teams\"");
        var requestId = Guid.NewGuid();
        await ActivateAsync(connectionString, folder.Path, ActivationActor.User("hoa", "Hoa Author", "Spring", "Teams rename", requestId));

        var history = await GetAsync(author, ReleasesUrl);

        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal([2, 1], items.Select(item => item.GetProperty("number").GetInt32()));
        var newest = items[0];
        Assert.Equal(
            ("hoa", "Hoa Author", "Spring", "Teams rename", requestId),
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

        using var anna = factory.CreateClient().SignedInAs("anna");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await anna.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(ReleasesUrl, Host), CancellationToken)).StatusCode);
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

    private static async Task ActivateAsync(string connectionString, string folder, ActivationActor actor)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration, SamplePackages.Versions, CancellationToken);
        Assert.DoesNotContain(compiled.Diagnostics, diagnostic => diagnostic.Severity == Axis.Configuration.Diagnostics.DiagnosticSeverity.Error);
        var bindings = new ActivationBindings(
            Tenant,
            new Dictionary<string, ActivationBinding>
            {
                ["PurchasingSystemUrl"] = new(ActivationBinding.UrlKind, new Uri("https://purchasing.example.test")),
                ["PurchasingSystemToken"] = new(ActivationBinding.SecretKind, null),
            },
            AllowHttp: false);
        var result = await ReleaseActivator.ActivateAsync(
            compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance,
            ReleaseActivator.DefaultInlineRecomputeLimit, actor, CancellationToken);
        Assert.Empty(result.Diagnostics);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString, string folder) =>
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
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Kind", "secret");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Value", "studio-test-token");
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("ActivateOnStartup:0", folder);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"), new TestIdentity("anna", "Anna", "employee"));
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
