using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class StudioEndpointTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private const string EntityFile = "entities/purchase-request.json";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"axis-studio-{Guid.NewGuid():N}");

    private string Folder => Path.Combine(_root, "purchase-requests");

    private string Outside => Path.Combine(_root, "outside");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), Folder);
        Directory.CreateDirectory(Outside);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Author_lists_every_json_file_with_its_version()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/resources", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        var expected = Directory.EnumerateFiles(Folder, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Folder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expected, items.Select(item => item.GetProperty("file").GetString()));
        foreach (var item in items)
        {
            var path = Path.Combine(Folder, item.GetProperty("file").GetString()!);
            Assert.Equal(Sha256(path), item.GetProperty("version").GetString());
            using var file = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal(file.RootElement.GetProperty("id").GetString(), item.GetProperty("id").GetString());
            Assert.Equal(file.RootElement.GetProperty("kind").GetString(), item.GetProperty("kind").GetString());
            Assert.Equal(file.RootElement.GetProperty("name").GetString(), item.GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task Author_reads_a_resource_by_id_in_either_letter_case()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        using var file = JsonDocument.Parse(File.ReadAllBytes(path));
        var id = file.RootElement.GetProperty("id").GetString()!;

        foreach (var form in new[] { id.ToLowerInvariant(), id.ToUpperInvariant() })
        {
            var response = await client.GetAsync(new Uri($"/api/studio/apps/purchaserequests/resources/{form}", UriKind.Relative), CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var version = Sha256(path);
            Assert.Equal($"\"{version}\"", response.Headers.ETag?.Tag);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            var root = body.RootElement;
            Assert.Equal(id.ToLowerInvariant(), root.GetProperty("id").GetString());
            Assert.Equal(EntityFile, root.GetProperty("file").GetString());
            Assert.Equal(version, root.GetProperty("version").GetString());
            Assert.Equal("entity", root.GetProperty("content").GetProperty("kind").GetString());
        }
    }

    [Theory]
    [InlineData("/api/studio/apps/PurchaseRequests/resources")]
    [InlineData("/api/studio/apps/PurchaseRequests/resources/00000000-0000-0000-0000-000000000001")]
    public async Task User_without_the_author_role_gets_403_and_no_session_gets_401(string url)
    {
        await using var factory = CreateFactory();
        using var anna = factory.CreateClient().SignedInAs("anna");
        using var anonymous = factory.CreateClient();

        var forbidden = await anna.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);
        var unauthorized = await anonymous.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("application/problem+json", forbidden.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("application/problem+json", unauthorized.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/api/studio/apps/PurchaseRequests/resources")]
    [InlineData("/api/studio/apps/PurchaseRequests/resources/00000000-0000-0000-0000-000000000001")]
    public async Task Studio_is_off_without_an_author_role_or_without_folders(string url)
    {
        foreach (var (folder, role) in new[] { (Folder, ""), ("", "author") })
        {
            await using var factory = CreateFactory(folder, role);
            using var client = factory.CreateClient().SignedInAs("hoa");

            var response = await client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Theory]
    [InlineData("/api/studio/apps/Unknown/resources", HttpStatusCode.NotFound)]
    [InlineData("/api/studio/apps/PURCHASEREQUESTS/resources", HttpStatusCode.OK)]
    [InlineData("/api/studio/apps/Unknown/resources/00000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    [InlineData("/api/studio/apps/PurchaseRequests/resources/00000000-0000-0000-0000-000000000001", HttpStatusCode.NotFound)]
    [InlineData("/api/studio/apps/PurchaseRequests/resources/..%2F..%2Fapplication", HttpStatusCode.NotFound)]
    [InlineData("/api/studio/apps/PurchaseRequests/resources/application.json", HttpStatusCode.NotFound)]
    public async Task Unknown_application_or_resource_is_404(string url, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.NotFound)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            Assert.DoesNotContain("Unknown", body, StringComparison.Ordinal);
            Assert.DoesNotContain("..", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Linked_files_and_folders_outside_the_folder_are_neither_listed_nor_readable()
    {
        var linkedFileId = Guid.NewGuid();
        var linkedFolderId = Guid.NewGuid();
        var outsideFile = Path.Combine(Outside, "outside.json");
        File.WriteAllText(outsideFile, $$"""{ "id": "{{linkedFileId}}", "kind": "entity", "name": "Outside" }""");
        Directory.CreateDirectory(Path.Combine(Outside, "folder"));
        File.WriteAllText(
            Path.Combine(Outside, "folder", "inside.json"),
            $$"""{ "id": "{{linkedFolderId}}", "kind": "entity", "name": "Outside folder" }""");
        File.CreateSymbolicLink(Path.Combine(Folder, "entities", "outside.json"), outsideFile);
        Directory.CreateSymbolicLink(Path.Combine(Folder, "linked"), Path.Combine(Outside, "folder"));
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var list = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/resources", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var text = await list.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain(linkedFileId.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain(linkedFolderId.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("outside.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("linked/", text, StringComparison.Ordinal);
        foreach (var id in new[] { linkedFileId, linkedFolderId })
        {
            var read = await client.GetAsync(new Uri($"/api/studio/apps/PurchaseRequests/resources/{id}", UriKind.Relative), CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }
    }

    [Fact]
    public async Task Files_without_an_id_and_kind_are_left_out_and_the_first_of_two_files_with_one_id_wins()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(Folder, "entities", "a-first.json"), $$"""{ "id": "{{id}}", "kind": "entity", "name": "First" }""");
        File.WriteAllText(Path.Combine(Folder, "entities", "b-second.json"), $$"""{ "id": "{{id}}", "kind": "entity", "name": "Second" }""");
        File.WriteAllText(Path.Combine(Folder, "entities", "no-id.json"), """{ "kind": "entity" }""");
        File.WriteAllText(Path.Combine(Folder, "entities", "broken.json"), "{ not json");
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri($"/api/studio/apps/PurchaseRequests/resources/{id}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("entities/a-first.json", body.RootElement.GetProperty("file").GetString());
        var list = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/resources", UriKind.Relative), CancellationToken);
        var text = await list.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("no-id.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("broken.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("b-second.json", text, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateFactory(string? folder = null, string authorRole = "author") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Studio:Folders:0", folder ?? Folder);
            builder.UseSetting("Studio:AuthorRole", authorRole);
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(
                new TestIdentity("hoa", "Hoa Author", "author"),
                new TestIdentity("anna", "Anna Employee", "employee"));
        });

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

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
