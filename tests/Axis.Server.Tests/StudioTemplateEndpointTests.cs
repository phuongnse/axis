using System.Net;
using System.Text.Json;
using Axis.Configuration.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class StudioTemplateEndpointTests : IDisposable
{
    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"axis-studio-templates-{Guid.NewGuid():N}");

    public StudioTemplateEndpointTests()
    {
        Directory.CreateDirectory(StudioFolder);
        Directory.CreateDirectory(TemplatesFolder);
    }

    private string StudioFolder => Path.Combine(_root, "studio");

    private string TemplatesFolder => Path.Combine(_root, "templates");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Author_lists_the_newest_version_of_each_template_sorted_by_id()
    {
        AddTemplate("tpl-b", "1.0.0");
        AddTemplate("tpl-a", "1.2.0");
        AddTemplate("tpl-a", "1.0.0");
        await using var factory = CreateFactory(TemplatesFolder);
        using var client = factory.CreateClient().SignedInAs("author");

        var response = await client.GetAsync(new Uri("/api/studio/templates", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(
            [("tpl-a", "1.2.0"), ("tpl-b", "1.0.0")],
            items.Select(item => (item.GetProperty("id").GetString(), item.GetProperty("version").GetString())));
        var first = items[0];
        Assert.Equal("Title of tpl-a", first.GetProperty("title").GetProperty("en").GetString());
        Assert.Equal("Tiêu đề của tpl-a", first.GetProperty("title").GetProperty("vi").GetString());
        Assert.Equal("Description of tpl-a", first.GetProperty("description").GetProperty("en").GetString());
        Assert.Equal("Mô tả của tpl-a", first.GetProperty("description").GetProperty("vi").GetString());
        Assert.All(items, item => Assert.Equal(JsonValueKind.Array, item.GetProperty("diagnostics").ValueKind));
        Assert.All(items, item => Assert.False(item.GetProperty("builtIn").GetBoolean()));
    }

    [Fact]
    public async Task Built_in_templates_are_listed_first_and_the_others_by_id()
    {
        AddTemplate("tpl-b", "1.0.0");
        AddTemplate("tpl-z", "1.0.0", builtIn: true);
        AddTemplate("tpl-a", "1.0.0");
        await using var factory = CreateFactory(TemplatesFolder);
        using var client = factory.CreateClient().SignedInAs("author");

        var response = await client.GetAsync(new Uri("/api/studio/templates", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["tpl-z", "tpl-a", "tpl-b"], items.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal([true, false, false], items.Select(item => item.GetProperty("builtIn").GetBoolean()));
    }

    [Fact]
    public async Task Without_Templates_Path_the_list_is_empty()
    {
        await using var factory = CreateFactory(null);
        using var client = factory.CreateClient().SignedInAs("author");

        var response = await client.GetAsync(new Uri("/api/studio/templates", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task User_without_the_author_role_gets_403_and_no_session_gets_401()
    {
        AddTemplate("tpl-a", "1.0.0");
        await using var factory = CreateFactory(TemplatesFolder);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();

        var forbidden = await requester.GetAsync(new Uri("/api/studio/templates", UriKind.Relative), CancellationToken);
        var unauthorized = await anonymous.GetAsync(new Uri("/api/studio/templates", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task A_Templates_Path_that_does_not_exist_stops_the_server_at_start()
    {
        var missing = Path.Combine(TemplatesFolder, "missing");
        await using var factory = CreateFactory(missing);

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("Templates:Path", exception.Message, StringComparison.Ordinal);
    }

    private void AddTemplate(string id, string version, bool builtIn = false)
    {
        var folder = Path.Combine(TemplatesFolder, id, version);
        var builtInLine = builtIn ? "\"builtIn\": true," : "";
        Write(folder, "template.json", $$"""
            {
              "kind": "applicationTemplate",
              "id": "{{id}}",
              "version": "{{version}}",
              {{builtInLine}}
              "title": { "en": "Title of {{id}}", "vi": "Tiêu đề của {{id}}" },
              "description": { "en": "Description of {{id}}", "vi": "Mô tả của {{id}}" }
            }
            """);
        Write(folder, "application.json", $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "Blank", "formatVersion": 1 }
            """);
        Write(folder, "sites/blank.json", $$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "kind": "site",
              "name": "Blank",
              "formatVersion": 1,
              "path": "blank",
              "title": { "textKey": "site.title" },
              "locales": { "default": "en", "fallback": "en", "available": ["en"] },
              "navigation": [{ "page": "Home", "label": { "textKey": "nav.home" } }]
            }
            """);
        Write(folder, "identity-providers/accounts.json", $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "identityProvider", "name": "accounts", "formatVersion": 1, "type": "accounts" }
            """);
    }

    private static void Write(string folder, string relativePath, string content)
    {
        var path = Path.Combine(folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private WebApplicationFactory<Program> CreateFactory(string? templatesPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            if (templatesPath is not null)
            {
                builder.UseSetting("Templates:Path", templatesPath);
            }

            builder.UseSetting("Studio:Folders:0", StudioFolder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
}
