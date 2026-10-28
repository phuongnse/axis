using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Axis.Server.Tests;

/// <summary>
/// Sends each kind's starter, the content Studio's New resource creates from, through the create route.
/// The starters are the SPA's <c>studioStarters.json</c>, so Vitest and this class test the same ones.
/// </summary>
public sealed class StudioStarterTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private const string SampleOidcProvider = """
        {
          "id": "b52449b4-bc18-4972-bf11-7b73f5a82198",
          "kind": "identityProvider",
          "name": "accounts",
          "formatVersion": 1,
          "type": "oidc",
          "label": { "textKey": "site.title" },
          "issuer": { "binding": "StarterIssuer" },
          "clientId": { "binding": "StarterClientId" },
          "clientSecret": { "binding": "StarterClientSecret" }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"axis-starters-{Guid.NewGuid():N}");

    private string Folder => Path.Combine(_root, "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string> StarterKinds()
    {
        var data = new TheoryData<string>();
        foreach (var kind in Starters().Select(pair => pair.Key))
        {
            data.Add(kind);
        }

        return data;
    }

    public ValueTask InitializeAsync()
    {
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public void Starters_exist_for_exactly_the_kinds_the_create_route_accepts()
    {
        var kinds = Starters().Select(pair => pair.Key).Order(StringComparer.Ordinal);

        Assert.Equal(StudioFileNames.Kinds.Order(StringComparer.Ordinal), kinds);
    }

    [Theory]
    [MemberData(nameof(StarterKinds))]
    public async Task Create_saves_the_starter_and_its_diagnostics_name_only_the_references_left_empty(string kind)
    {
        var starter = Starters().Single(pair => pair.Key == kind).Value;
        var empty = starter["emptyReferences"]!.AsArray().Select(pointer => pointer!.GetValue<string>()).ToList();
        var name = kind == "role" ? "new-role" : "NewThing";
        var id = Guid.NewGuid().ToString();
        var body = new JsonObject { ["id"] = id, ["kind"] = kind, ["name"] = name, ["formatVersion"] = 1 };
        var texts = new JsonObject();
        if (kind == "text")
        {
            // New resource copies the texts of an existing locale into a new one, so no key drifts.
            body["locale"] = "de";
            texts = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Folder, "texts", "en.json")))!["texts"]!.AsObject();
        }

        if (kind == "site")
        {
            body["path"] = StudioFileNames.Kebab(name);
        }

        foreach (var (key, value) in starter["content"]!.AsObject())
        {
            body[key] = key == "texts" ? texts.DeepClone() : value?.DeepClone();
        }

        if (kind == "identityProvider")
        {
            // The sample already has an accounts provider, and an application has at most one (AXC0288). The
            // starter is the accounts provider, so the sample's becomes an oidc one and the starter is the only one.
            await File.WriteAllTextAsync(
                Path.Combine(Folder, "identity-providers", "accounts.json"),
                SampleOidcProvider,
                CancellationToken);
        }

        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("author");
        var before = await Diagnostics(client);

        var response = await Create(client, body.ToJsonString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var file = answer.RootElement.GetProperty("file").GetString()!;
        Assert.True(File.Exists(Path.Combine(Folder, file)));
        var after = answer.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        var own = after.Where(item => item.GetProperty("resourceId").GetString() == id).ToList();
        foreach (var diagnostic in own)
        {
            Assert.True(empty.Contains(diagnostic.GetProperty("path").GetString()!), diagnostic.ToString());
        }

        // The create changes no diagnostic of another resource.
        var others = after.Where(item => item.GetProperty("resourceId").GetString() != id).Select(Describe).Order(StringComparer.Ordinal);
        Assert.Equal(before.Select(Describe).Order(StringComparer.Ordinal), others);
    }

    private static string Describe(JsonElement diagnostic) =>
        string.Join('|', diagnostic.GetProperty("code").GetString(), diagnostic.GetProperty("file").GetString(), diagnostic.GetProperty("path").GetString());

    private static IEnumerable<KeyValuePair<string, JsonNode>> Starters()
    {
        var json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Starters", "studioStarters.json")))!.AsObject();
        return json.Select(pair => KeyValuePair.Create(pair.Key, pair.Value!));
    }

    private static async Task<List<JsonElement>> Diagnostics(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/diagnostics", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("diagnostics").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static async Task<HttpResponseMessage> Create(HttpClient client, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/studio/apps/PurchaseRequests/resources", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Studio:Folders:0", Folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"));
        });

    private static void CopyDirectory(string source, string target)
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
}
