using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The catalog, bindings and relaxed routes of the Studio resource form answer what the server installed and configured, to an
/// author alone, and the bindings route answers names and never a value.
/// </summary>
public sealed class StudioResourceFormRouteTests : IAsyncLifetime
{
    private const string Tenant = "a";

    private const string Host = "a.example.test";

    private const string CatalogUrl = "/api/studio/catalog";

    private const string BindingsUrl = "/api/studio/bindings";

    private const string RelaxedUrl = "/api/studio/apps/ModuleRelaxApp/relaxed";

    private static readonly Dictionary<string, (string Kind, string Value)> _bindings = new()
    {
        ["OrdersUrl"] = ("url", "https://orders-9f3a1c.example.test/private-path"),
        ["BillingUrl"] = ("url", "https://billing-4be72d.example.test/private-path"),
        ["BillingToken"] = ("secret", "secret-value-c81d05e7"),
        ["Attachments"] = ("storage", "database"),
    };

    private readonly PostgreSqlFixture _database = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _database.InitializeAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_catalog_lists_modules_and_packages_with_versions_newest_first_slot_titles_and_time_zones_by_offset_then_id()
    {
        await using var setup = await StartAsync("studio_catalog");

        var catalog = await GetAsync(setup.Author, CatalogUrl);

        var modules = catalog.GetProperty("modules").EnumerateArray().ToList();
        Assert.Equal(modules.Select(module => module.GetProperty("id").GetString()).Order(StringComparer.Ordinal), modules.Select(module => module.GetProperty("id").GetString()));
        var requests = modules.Single(module => module.GetProperty("id").GetString() == "requests");
        Assert.Equal(["1.1.0", "1.0.0"], requests.GetProperty("versions").EnumerateArray().Select(version => version.GetString()));
        Assert.Equal("Requests", requests.GetProperty("label").GetString());
        var claims = modules.Single(module => module.GetProperty("id").GetString() == "claims");
        Assert.Equal(["1.0.0"], claims.GetProperty("versions").EnumerateArray().Select(version => version.GetString()));
        Assert.Equal(
            [("filer", "Filer"), ("viewer", "Viewer")],
            claims.GetProperty("roles").EnumerateArray().Select(slot => (slot.GetProperty("name").GetString()!, slot.GetProperty("titles").GetProperty("en").GetString()!)));
        Assert.Equal(0, claims.GetProperty("connectors").GetArrayLength());

        var package = catalog.GetProperty("packages").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "payment-references");
        Assert.NotEmpty(package.GetProperty("label").GetString()!);
        Assert.NotEmpty(package.GetProperty("versions").EnumerateArray());

        var zones = catalog.GetProperty("timeZones").EnumerateArray().Select(zone => (Id: zone.GetProperty("id").GetString()!, Offset: zone.GetProperty("offset").GetInt32())).ToList();
        Assert.Equal(zones.OrderBy(zone => zone.Offset).ThenBy(zone => zone.Id, StringComparer.Ordinal), zones);
        Assert.Contains(("Asia/Ho_Chi_Minh", 420), zones);
        Assert.DoesNotContain(zones, zone => !TimeZoneInfo.TryFindSystemTimeZoneById(zone.Id, out var found) || !found.HasIanaId);
    }

    [Fact]
    public async Task The_bindings_answer_lists_the_names_by_kind_and_holds_no_value()
    {
        await using var setup = await StartAsync("studio_bindings");

        using var request = RecordApiFixture.Request(BindingsUrl, Host);
        using var response = await setup.Author.SendAsync(request, CancellationToken);
        var text = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(text);
        var names = (string kind) => body.RootElement.GetProperty(kind).EnumerateArray().Select(name => name.GetString()).ToList();
        Assert.Equal(["BillingUrl", "OrdersUrl"], names("url"));
        Assert.Equal(["BillingToken"], names("secret"));
        Assert.Equal(["Attachments"], names("storage"));
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
        foreach (var (_, value) in _bindings.Values)
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_relaxed_route_answers_the_folders_entries_in_manifest_order_with_words_and_the_contract_id()
    {
        await using var setup = await StartAsync("studio_relaxed");

        var relaxed = await GetAsync(setup.Author, RelaxedUrl);

        var items = relaxed.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [("requests", "Request/transitions/submit"), ("stages", "Ticket/validations/amountFrozenAfterSubmit"), ("ledgers", "Claim/effects/entryRecorded"), ("claims", "Claim/ceiling/viewer")],
            items.Select(item => (item.GetProperty("module").GetString()!, item.GetProperty("point").GetString()!)));
        var requests = items[0];
        Assert.Equal("This application accepts requests without an amount.", requests.GetProperty("reason").GetString());
        Assert.Equal(("Requests", "transition"), (requests.GetProperty("moduleTitle").GetString(), requests.GetProperty("part").GetString()));
        Assert.Equal("Submit", requests.GetProperty("titles").GetProperty("en").GetString());
        Assert.Equal("Request", requests.GetProperty("entityTitles").GetProperty("en").GetString());
        Assert.Equal(["transition", "validation", "effect", "ceiling"], items.Select(item => item.GetProperty("part").GetString()));
        Assert.Equal("Viewer", items[3].GetProperty("titles").GetProperty("en").GetString());

        var contract = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(FixtureModules, "requests", "1.0.0", "contracts", "request.json"), CancellationToken));
        Assert.Equal(contract.RootElement.GetProperty("id").GetString(), requests.GetProperty("contractId").GetString());
        Assert.All(items, item => Assert.True(Guid.TryParseExact(item.GetProperty("contractId").GetString(), "D", out _)));
    }

    [Theory]
    [InlineData(CatalogUrl)]
    [InlineData(BindingsUrl)]
    [InlineData(RelaxedUrl)]
    public async Task Each_route_refuses_a_user_without_the_author_role_and_a_request_without_a_session(string url)
    {
        await using var setup = await StartAsync("studio_form_refused");
        using var requester = setup.Factory.CreateClient().SignedInAs("requester");
        using var anonymous = setup.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await requester.SendAsync(RecordApiFixture.Request(url, Host), CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(RecordApiFixture.Request(url, Host), CancellationToken)).StatusCode);
    }

    private static string FixtureModules => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Modules");

    private async Task<Setup> StartAsync(string database)
    {
        var folder = new TemporaryFolder();
        var modules = new TemporaryFolder();
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModuleRelaxApp"), folder.Path);
        StudioReleaseHistoryTests.CopyDirectory(FixtureModules, modules.Path);
        var newer = Path.Combine(modules.Path, "requests", "1.1.0");
        StudioReleaseHistoryTests.CopyDirectory(Path.Combine(modules.Path, "requests", "1.0.0"), Directory.CreateDirectory(newer).FullName);
        var manifest = Path.Combine(newer, "module.json");
        await File.WriteAllTextAsync(manifest, (await File.ReadAllTextAsync(manifest, CancellationToken)).Replace("\"1.0.0\"", "\"1.1.0\"", StringComparison.Ordinal), CancellationToken);
        var connectionString = await _database.CreateDatabaseAsync(database, CancellationToken);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(Tenant);
            foreach (var (name, (kind, value)) in _bindings)
            {
                builder.UseSetting($"Tenants:{Tenant}:Bindings:{name}:Kind", kind);
                builder.UseSetting($"Tenants:{Tenant}:Bindings:{name}:Value", value);
            }

            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Modules:Path", modules.Path);
            builder.UseSetting("Studio:Folders:0", folder.Path);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"), new TestIdentity("requester", "Requester", "employee"));
        });
        return new Setup(factory, factory.CreateClient().SignedInAs("author"), [folder, modules]);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        using var request = RecordApiFixture.Request(url, Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.Clone();
    }

    private sealed class Setup(WebApplicationFactory<Program> factory, HttpClient author, IReadOnlyList<IDisposable> folders) : IAsyncDisposable
    {
        public WebApplicationFactory<Program> Factory { get; } = factory;

        public HttpClient Author { get; } = author;

        public async ValueTask DisposeAsync()
        {
            Author.Dispose();
            await Factory.DisposeAsync();
            foreach (var folder in folders)
            {
                folder.Dispose();
            }
        }
    }
}
