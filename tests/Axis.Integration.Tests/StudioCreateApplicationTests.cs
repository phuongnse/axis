using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Studio;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>POST /api/studio/apps</c> creates an application folder in <c>Studio:ApplicationsPath</c> under the write lock,
/// through a hidden temporary folder that is renamed. It never writes outside that folder, never replaces an
/// application and leaves nothing behind when it fails.
/// </summary>
public sealed class StudioCreateApplicationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TemplateId = "request-approval";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_blank_create_answers_201_with_no_diagnostics_and_the_application_is_listed()
    {
        using var setup = await Setup.StartAsync(database);
        using var client = setup.Factory.CreateClient().SignedInAs("author");

        using var response = await PostAsync(client, """{ "name": "PurchaseOrders" }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/studio/apps/PurchaseOrders/resources", response.Headers.Location?.OriginalString);
        using var body = await ReadAsync(response);
        Assert.Equal("PurchaseOrders", body.RootElement.GetProperty("name").GetString());
        Assert.Empty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
        var folder = Path.Combine(setup.ApplicationsPath, "purchase-orders");
        Assert.Equal(
            ["application.json", "identity-providers/accounts.json", "pages/home.json", "sites/purchase-orders.json", "texts/en.json"],
            Files(folder));
        Assert.Equal([Path.Combine(setup.ApplicationsPath, "purchase-orders")], Directory.GetFileSystemEntries(setup.ApplicationsPath));

        using var list = await GetAsync(client, "/api/studio/apps");
        Assert.Equal(
            ["PurchaseRequests", "PurchaseOrders"],
            list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
        using var diagnostics = await GetAsync(client, "/api/studio/apps/purchaseorders/diagnostics");
        Assert.Empty(diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray());

        using var site = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "sites/purchase-orders.json"), CancellationToken));
        Assert.Equal("purchase-orders", site.RootElement.GetProperty("path").GetString());
        using var home = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "pages/home.json"), CancellationToken));
        Assert.Equal("taskInbox1", home.RootElement.GetProperty("widgets")[0].GetProperty("name").GetString());
        using var texts = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "texts/en.json"), CancellationToken));
        Assert.Equal("PurchaseOrders", texts.RootElement.GetProperty("texts").GetProperty("site.title").GetString());
        Assert.Equal(5, Files(folder).Select(file => IdOf(Path.Combine(folder, file))).Distinct().Count());
    }

    [Fact]
    public async Task A_create_from_a_template_copies_every_file_with_new_ids_and_the_same_diagnostics()
    {
        using var setup = await Setup.StartAsync(database);
        using var client = setup.Factory.CreateClient().SignedInAs("author");
        var templateFolder = Path.Combine(setup.TemplatesPath, TemplateId, "1.0.0");

        using var response = await PostAsync(client, $$"""{ "name": "LeaveRequests", "template": "{{TemplateId}}" }""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        var folder = Path.Combine(setup.ApplicationsPath, "leave-requests");
        var expectedFiles = Files(templateFolder).Where(file => file != "template.json").ToList();
        Assert.Equal(expectedFiles, Files(folder));
        foreach (var file in expectedFiles)
        {
            Assert.NotEqual(IdOf(Path.Combine(templateFolder, file)), IdOf(Path.Combine(folder, file)));
        }

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "application.json"), CancellationToken));
        Assert.Equal("LeaveRequests", manifest.RootElement.GetProperty("name").GetString());
        using var site = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "sites/blank.json"), CancellationToken));
        Assert.Equal("leave-requests", site.RootElement.GetProperty("path").GetString());
        using var originalPage = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(templateFolder, "pages/home.json"), CancellationToken));
        using var copiedPage = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "pages/home.json"), CancellationToken));
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse(originalPage.RootElement.GetProperty("widgets").GetRawText()),
            JsonNode.Parse(copiedPage.RootElement.GetProperty("widgets").GetRawText())));

        using var templates = await GetAsync(client, "/api/studio/templates");
        var template = Assert.Single(templates.RootElement.GetProperty("items").EnumerateArray());
        var expected = Key(template.GetProperty("diagnostics"));
        Assert.Contains(expected, entry => entry.Severity == "error");
        Assert.Equal(expected, Key(body.RootElement.GetProperty("diagnostics")));
        using var listed = await GetAsync(client, "/api/studio/apps/LeaveRequests/diagnostics");
        Assert.Equal(expected, Key(listed.RootElement.GetProperty("diagnostics")));
    }

    [Fact]
    public async Task A_name_that_is_taken_or_whose_folder_exists_answers_409_and_writes_nothing()
    {
        using var setup = await Setup.StartAsync(database);
        using var client = setup.Factory.CreateClient().SignedInAs("author");
        Directory.CreateDirectory(Path.Combine(setup.ApplicationsPath, "taken-folder"));
        using var first = await PostAsync(client, """{ "name": "PurchaseOrders" }""");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var before = Entries(setup.ApplicationsPath);

        foreach (var name in new[] { "PurchaseRequests", "purchaserequests", "PurchaseOrders", "PURCHASEORDERS", "TakenFolder" })
        {
            using var response = await PostAsync(client, $$"""{ "name": "{{name}}" }""");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("An application with this name or folder already exists.", (await ReadAsync(response)).RootElement.GetProperty("title").GetString());
            Assert.Equal(before, Entries(setup.ApplicationsPath));
        }
    }

    [Fact]
    public async Task A_bad_body_answers_400_or_415_or_404_and_writes_nothing()
    {
        using var setup = await Setup.StartAsync(database);
        using var client = setup.Factory.CreateClient().SignedInAs("author");
        var longName = "A" + string.Concat(Enumerable.Repeat("bC", 29));

        var cases = new (string Body, HttpStatusCode Status)[]
        {
            ("""{ "name": "../x" }""", HttpStatusCode.BadRequest),
            ("""{ "name": "a/b" }""", HttpStatusCode.BadRequest),
            ("""{ "name": "1abc" }""", HttpStatusCode.BadRequest),
            ("""{ "name": "" }""", HttpStatusCode.BadRequest),
            ("""{ "name": 5 }""", HttpStatusCode.BadRequest),
            ("""{ }""", HttpStatusCode.BadRequest),
            ("""[ "Name" ]""", HttpStatusCode.BadRequest),
            ("""not json""", HttpStatusCode.BadRequest),
            ("""{ "name": "Fine", "other": 1 }""", HttpStatusCode.BadRequest),
            ("""{ "name": "Studio" }""", HttpStatusCode.BadRequest),
            ("""{ "name": "Api" }""", HttpStatusCode.BadRequest),
            ($$"""{ "name": "{{longName}}" }""", HttpStatusCode.BadRequest),
            ("""{ "name": "Fine", "template": null }""", HttpStatusCode.BadRequest),
            ("""{ "name": "Fine", "template": 5 }""", HttpStatusCode.BadRequest),
            ("""{ "name": "Fine", "template": "no-such-template" }""", HttpStatusCode.NotFound),
            ("""{ "name": "Fine", "template": "Request-Approval" }""", HttpStatusCode.NotFound),
        };
        foreach (var (body, status) in cases)
        {
            using var response = await PostAsync(client, body);

            Assert.Equal(status, response.StatusCode);
            Assert.Empty(Directory.GetFileSystemEntries(setup.ApplicationsPath));
        }

        using var content = new StringContent("""{ "name": "Fine" }""", Encoding.UTF8, "text/plain");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/studio/apps", UriKind.Relative)) { Content = content };
        request.Headers.Add("X-Axis-Csrf", "1");
        using var unsupported = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupported.StatusCode);
        Assert.Empty(Directory.GetFileSystemEntries(setup.ApplicationsPath));
    }

    [Fact]
    public async Task The_route_answers_401_without_a_session_and_403_without_the_author_role_the_CSRF_header_or_Studio_ApplicationsPath()
    {
        using var setup = await Setup.StartAsync(database);
        using var anonymous = setup.Factory.CreateClient();
        using var requester = setup.Factory.CreateClient().SignedInAs("requester");
        using var author = setup.Factory.CreateClient().SignedInAs("author");

        using var unauthorized = await PostAsync(anonymous, """{ "name": "Fine" }""");
        using var notAuthor = await PostAsync(requester, """{ "name": "Fine" }""");
        using var noCsrf = await PostAsync(author, """{ "name": "Fine" }""", csrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, notAuthor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Empty(Directory.GetFileSystemEntries(setup.ApplicationsPath));

        using var withoutPath = await Setup.StartAsync(database, applicationsPath: false);
        using var withoutPathAuthor = withoutPath.Factory.CreateClient().SignedInAs("author");
        using var refused = await PostAsync(withoutPathAuthor, """{ "name": "Fine" }""");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("Studio cannot create applications here.", (await ReadAsync(refused)).RootElement.GetProperty("title").GetString());
        Assert.Empty(Directory.GetFileSystemEntries(withoutPath.ApplicationsPath));
    }

    [Fact]
    public async Task A_create_whose_second_file_fails_leaves_nothing_in_the_applications_path()
    {
        using var setup = await Setup.StartAsync(database, writer: new FailingOnSecondFileWriter());
        using var client = setup.Factory.CreateClient().SignedInAs("author");

        using var response = await PostAsync(client, """{ "name": "PurchaseOrders" }""");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(Directory.GetFileSystemEntries(setup.ApplicationsPath));
        using var list = await GetAsync(client, "/api/studio/apps");
        Assert.Equal(
            ["PurchaseRequests"],
            list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string body, bool csrf = true)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/studio/apps", UriKind.Relative)) { Content = content };
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static List<string> Files(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)];

    private static List<string> Entries(string folder) =>
        [.. Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path) + (File.Exists(path) ? ":" + Convert.ToHexString(File.ReadAllBytes(path)) : "/"))
            .Order(StringComparer.Ordinal)];

    private static string IdOf(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("id").GetString()!;
    }

    // The diagnostics by code, severity, file and path. The message and the resource id differ by design.
    private static List<(string Code, string Severity, string File, string Path)> Key(JsonElement diagnostics) =>
        [.. diagnostics.EnumerateArray()
            .Select(item => (
                item.GetProperty("code").GetString()!,
                item.GetProperty("severity").GetString()!,
                item.GetProperty("file").GetString()!,
                item.GetProperty("path").GetString()!))
            .Order()];

    private sealed class FailingOnSecondFileWriter : IStudioFileWriter
    {
        private int _writes;

        public async Task WriteAsync(string path, byte[] bytes, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _writes) == 2)
            {
                throw new IOException("The disk is full.");
            }

            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }
    }

    private sealed class Setup : IDisposable
    {
        private readonly TemporaryFolder _applications = new();
        private readonly TemporaryFolder _templates = new();
        private readonly TemporaryFolder _folder = StudioPublishTests.NewFolder();

        private Setup(WebApplicationFactory<Program> factory) => Factory = factory;

        public WebApplicationFactory<Program> Factory { get; private set; }

        public string ApplicationsPath => _applications.Path;

        public string TemplatesPath => _templates.Path;

        public static async Task<Setup> StartAsync(PostgreSqlFixture database, bool applicationsPath = true, IStudioFileWriter? writer = null)
        {
            var platform = await database.CreateDatabaseAsync("studio_create", CancellationToken);
            var setup = new Setup(null!);
            AddTemplate(setup._templates.Path);
            setup.Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseTestPlatform(platform);
                builder.UseSetting("Tenants:default:Hosts:0", "localhost");
                builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
                builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
                builder.UseSetting("Studio:Folders:0", setup._folder.Path);
                builder.UseSetting("Modules:Path", SampleModules.Path);
                builder.UseSetting("Studio:AuthorRole", "author");
                if (applicationsPath)
                {
                    builder.UseSetting("Studio:ApplicationsPath", setup._applications.Path);
                }

                builder.UseSetting("Templates:Path", setup._templates.Path);
                builder.UseTestOidc("default");
                builder.UseTestAuthentication(
                    new TestIdentity("author", "Author User", "author"),
                    new TestIdentity("requester", "Requester User", "employee"));
                if (writer is not null)
                {
                    builder.ConfigureTestServices(services => services.AddSingleton(writer));
                }
            });
            _ = setup.Factory.CreateClient();
            return setup;
        }

        public void Dispose()
        {
            Factory.Dispose();
            _applications.Dispose();
            _templates.Dispose();
            _folder.Dispose();
        }

        // A template with a page, an en text and a form that names an entity that does not exist, so it has an error.
        private static void AddTemplate(string root)
        {
            var folder = Path.Combine(root, TemplateId, "1.0.0");
            Write(folder, "template.json", $$"""
                {
                  "kind": "applicationTemplate",
                  "id": "{{TemplateId}}",
                  "version": "1.0.0",
                  "title": { "en": "Request approval", "vi": "Phê duyệt yêu cầu" },
                  "description": { "en": "A request approval.", "vi": "Một phê duyệt yêu cầu." }
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
                {
                  "id": "{{Guid.NewGuid()}}",
                  "kind": "identityProvider",
                  "name": "accounts",
                  "formatVersion": 1,
                  "type": "accounts"
                }
                """);
            Write(folder, "pages/home.json", $$"""
                {
                  "id": "{{Guid.NewGuid()}}",
                  "kind": "page",
                  "name": "Home",
                  "formatVersion": 1,
                  "title": { "textKey": "pages.home.title" },
                  "widgets": [{ "type": "taskInbox", "name": "taskInbox" }]
                }
                """);
            Write(folder, "texts/en.json", $$"""
                {
                  "id": "{{Guid.NewGuid()}}",
                  "kind": "text",
                  "name": "TextsEn",
                  "formatVersion": 1,
                  "locale": "en",
                  "texts": { "site.title": "Blank", "pages.home.title": "Home", "nav.home": "Home" }
                }
                """);
            Write(folder, "forms/broken.json", $$"""
                {
                  "id": "{{Guid.NewGuid()}}",
                  "kind": "form",
                  "name": "Broken",
                  "formatVersion": 1,
                  "entity": "NoSuchEntity",
                  "sections": [{ "title": { "textKey": "pages.home.title" }, "fields": [{ "field": "title" }] }]
                }
                """);
        }

        private static void Write(string folder, string relativePath, string content)
        {
            var path = Path.Combine(folder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
    }
}
