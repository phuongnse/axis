using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// A text write sets one text key in the <c>en</c> and <c>vi</c> text resources of a folder in one request. The
/// platform database holds the folder's write lock, so these tests run on real PostgreSQL.
/// </summary>
public sealed class StudioTextWriteTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Url = "/api/studio/apps/PurchaseRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_key_goes_to_the_end_of_both_locales_and_the_answer_holds_the_versions_and_the_diagnostics()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");

        using var response = await PutAsync(client, "greeting.new", """{"texts":{"en":"Hello","vi":"Xin chào"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var resources = body.RootElement.GetProperty("resources").EnumerateArray().ToList();
        Assert.Equal(["en", "vi"], resources.Select(item => item.GetProperty("locale").GetString()));
        foreach (var (locale, text) in new[] { ("en", "Hello"), ("vi", "Xin chào") })
        {
            var path = Path.Combine(folder.Path, "texts", $"{locale}.json");
            var texts = (JsonObject)JsonNode.Parse(File.ReadAllBytes(path))!["texts"]!;
            Assert.Equal("greeting.new", texts.Last().Key);
            Assert.Equal(text, (string?)texts["greeting.new"]);
            var resource = resources.Single(item => item.GetProperty("locale").GetString() == locale);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), resource.GetProperty("version").GetString());
            Assert.Equal((string?)JsonNode.Parse(File.ReadAllBytes(path))!["id"], resource.GetProperty("id").GetString());
        }

        using var diagnostics = await client.GetAsync(new Uri($"{Url}/diagnostics", UriKind.Relative), CancellationToken);
        using var expected = JsonDocument.Parse(await diagnostics.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(
            expected.RootElement.GetProperty("diagnostics").GetRawText(),
            body.RootElement.GetProperty("diagnostics").GetRawText());
    }

    [Fact]
    public async Task An_existing_key_changes_only_its_value_and_keeps_every_other_key_in_order()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = new Dictionary<string, JsonObject>();
        foreach (var locale in new[] { "en", "vi" })
        {
            before[locale] = (JsonObject)JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder.Path, "texts", $"{locale}.json")))!["texts"]!;
        }

        using var response = await PutAsync(client, "nav.departments", """{"texts":{"en":"Teams","vi":"Nhóm"}}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var (locale, text) in new[] { ("en", "Teams"), ("vi", "Nhóm") })
        {
            var after = (JsonObject)JsonNode.Parse(File.ReadAllBytes(Path.Combine(folder.Path, "texts", $"{locale}.json")))!["texts"]!;
            Assert.Equal(before[locale].Select(pair => pair.Key), after.Select(pair => pair.Key));
            foreach (var (key, value) in before[locale].Where(pair => pair.Key != "nav.departments"))
            {
                Assert.Equal((string?)value, (string?)after[key]);
            }

            Assert.Equal(text, (string?)after["nav.departments"]);
            Assert.NotEqual((string?)before[locale]["nav.departments"], (string?)after["nav.departments"]);
        }
    }

    [Fact]
    public async Task A_body_without_a_locale_answers_400_and_changes_neither_file()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var client = factory.CreateClient().SignedInAs("author");
        var before = Snapshot(folder.Path);

        using var response = await PutAsync(client, "greeting.new", """{"texts":{"en":"Hello"}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    [Fact]
    public async Task A_session_without_the_author_role_gets_403_and_no_session_gets_401_and_no_file_changes()
    {
        using var folder = StudioPublishTests.NewFolder();
        await using var factory = await CreateFactoryAsync(folder.Path);
        using var requester = factory.CreateClient().SignedInAs("requester");
        using var anonymous = factory.CreateClient();
        var before = Snapshot(folder.Path);
        const string body = """{"texts":{"en":"Hello","vi":"Xin chào"}}""";

        using var forbidden = await PutAsync(requester, "greeting.new", body);
        using var unauthorized = await PutAsync(anonymous, "greeting.new", body);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(before, Snapshot(folder.Path));
    }

    internal static async Task<HttpResponseMessage> PutAsync(HttpClient client, string key, string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"{Url}/texts/{key}", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return await client.SendAsync(request, CancellationToken);
    }

    private static List<string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(folder, path) + ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))))
            .ToList();

    private async Task<WebApplicationFactory<Program>> CreateFactoryAsync(string folder)
    {
        var platform = await database.CreateDatabaseAsync("studio_texts", CancellationToken);
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
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("author", "Author User", "author"),
                new TestIdentity("requester", "Requester User", "employee"));
        });
    }
}
