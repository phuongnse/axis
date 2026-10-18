using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Modules;
using Axis.Configuration.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class StudioEndpointTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private const string EntityFile = "entities/purchase-request.json";

    private const string EntityId = "d9ccbf89-5004-4d52-8885-2ee377b7caa5";

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
    [InlineData("/api/studio/apps/PurchaseRequests/diagnostics")]
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
    [InlineData("/api/studio/apps/PurchaseRequests/diagnostics")]
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
    [InlineData("/api/studio/apps/Unknown/diagnostics", HttpStatusCode.NotFound)]
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

    [Fact]
    public async Task Author_gets_no_errors_for_a_valid_folder()
    {
        // The factory's databases are unreachable, so a 200 also shows that the route opens no connection.
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/diagnostics", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var diagnostics = body.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.DoesNotContain(diagnostics, item => item.GetProperty("severity").GetString() == "error");
    }

    [Fact]
    public async Task Author_gets_every_diagnostic_with_the_codes_activation_reports()
    {
        var entityPath = Path.Combine(Folder, EntityFile);
        var entity = File.ReadAllText(entityPath);
        const string target = "\"target\": \"Department\"";
        Assert.Contains(target, entity, StringComparison.Ordinal);
        File.WriteAllText(entityPath, entity.Replace(target, "\"target\": \"NoSuchEntity\"", StringComparison.Ordinal));
        const string scenarioFile = "scenarios/returned-request.json";
        var scenarioPath = Path.Combine(Folder, scenarioFile);
        File.WriteAllText(scenarioPath, File.ReadAllText(scenarioPath).Replace("\"formatVersion\": 1,", "\"formatVersion\": 1,\n  \"noSuchProperty\": true,", StringComparison.Ordinal));
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/diagnostics", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var actual = body.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(item => (
                item.GetProperty("code").GetString()!,
                item.GetProperty("severity").GetString()!,
                item.GetProperty("message").GetString()!,
                item.GetProperty("file").GetString()!,
                item.GetProperty("path").GetString()!,
                item.GetProperty("resourceId").GetString()))
            .ToList();
        Assert.Contains(actual, item => item.Item1 == DiagnosticCodes.UnknownReferenceTarget && item.Item4 == EntityFile);
        Assert.Contains(actual, item => item.Item1 == DiagnosticCodes.SchemaViolation && item.Item4 == scenarioFile);
        var expected = ApplicationCompiler.Compile(Folder, SamplePackages.Versions).Diagnostics
            .Select(diagnostic => (
                diagnostic.Code,
                diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "error",
                diagnostic.Message,
                diagnostic.File,
                diagnostic.Path,
                diagnostic.ResourceId?.ToString("D")))
            .ToList();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Diagnostics_compile_against_the_installed_modules()
    {
        var modules = Path.Combine(_root, "modules");
        Directory.CreateDirectory(Path.Combine(modules, "cost-centers", "1.0.0"));
        File.WriteAllText(
            Path.Combine(modules, "cost-centers", "1.0.0", "module.json"),
            """{ "kind": "module", "id": "cost-centers", "version": "1.0.0", "label": "Cost centers" }""");
        File.WriteAllText(
            Path.Combine(modules, "cost-centers", "1.0.0", "CostCenter.json"),
            $$"""
            { "id": "{{Guid.NewGuid()}}", "kind": "entity", "name": "CostCenter", "formatVersion": 1,
              "fields": [ { "name": "name", "type": "text", "required": true } ] }
            """);
        var applicationPath = Path.Combine(Folder, "application.json");
        File.WriteAllText(
            applicationPath,
            File.ReadAllText(applicationPath).Replace(
                "\"formatVersion\": 1,",
                "\"formatVersion\": 1,\n  \"modules\": [{ \"id\": \"cost-centers\", \"version\": \"^1.0.0\" }],",
                StringComparison.Ordinal));
        var withoutModules = ApplicationCompiler.Compile(Folder, SamplePackages.Versions).Diagnostics;
        Assert.Contains(withoutModules, diagnostic => diagnostic.Code == DiagnosticCodes.UnknownModule);
        await using var factory = CreateFactory(modulesPath: modules);
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await client.GetAsync(new Uri("/api/studio/apps/PurchaseRequests/diagnostics", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var codes = body.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)
            .ToList();
        Assert.DoesNotContain(DiagnosticCodes.UnknownModule, codes);
        var expected = ApplicationCompiler.Compile(Folder, SamplePackages.Versions, ModuleCatalog.Load(modules).Modules).Diagnostics
            .Select(diagnostic => diagnostic.Code)
            .ToList();
        Assert.Equal(expected, codes);
    }

    [Fact]
    public async Task Patch_with_the_current_version_changes_only_the_patched_value_and_keeps_key_order()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        var before = File.ReadAllBytes(path);
        var version = Sha256(path);

        var response = await Patch(client, """[{"op":"replace","path":"/displayField","value":"number"}]""", $"\"{version}\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = File.ReadAllBytes(path);
        var newVersion = Sha256(path);
        Assert.NotEqual(version, newVersion);
        Assert.Equal($"\"{newVersion}\"", response.Headers.ETag?.Tag);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(newVersion, body.RootElement.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("diagnostics").ValueKind);
        var original = JsonNode.Parse(before)!.AsObject();
        var patched = JsonNode.Parse(after)!.AsObject();
        Assert.Equal(original.Select(pair => pair.Key), patched.Select(pair => pair.Key));
        Assert.Equal("number", patched["displayField"]!.GetValue<string>());
        original["displayField"] = "number";
        Assert.True(JsonNode.DeepEquals(original, patched));
        var text = Encoding.UTF8.GetString(after);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Folder, ".*.tmp", SearchOption.AllDirectories));

        var second = await Patch(client, """[{"op":"replace","path":"/displayField","value":"title"}]""", newVersion);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstLines = text.Split('\n');
        var secondLines = File.ReadAllText(path).Split('\n');
        Assert.Equal(firstLines.Length, secondLines.Length);
        Assert.Equal(1, firstLines.Zip(secondLines).Count(pair => pair.First != pair.Second));
    }

    [Fact]
    public async Task Patch_with_a_stale_version_gets_409_and_leaves_the_file_unchanged()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        var old = Sha256(path);
        var first = await Patch(client, """[{"op":"replace","path":"/displayField","value":"number"}]""", old);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterFirst = File.ReadAllBytes(path);

        var second = await Patch(client, """[{"op":"replace","path":"/displayField","value":"title"}]""", old);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal(afterFirst, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"{0}\"")]
    [InlineData("\"{0}\", \"{0}\"")]
    public async Task Patch_with_a_wildcard_weak_or_listed_version_is_stale(string ifMatch)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        var before = File.ReadAllBytes(path);

        var response = await Patch(client, """[{"op":"replace","path":"/displayField","value":"number"}]""", string.Format(System.Globalization.CultureInfo.InvariantCulture, ifMatch, Sha256(path)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Patch_accepts_the_bare_version_and_either_letter_case_of_the_id()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Patch(client, """[{"op":"replace","path":"/displayField","value":"number"}]""", Sha256(Path.Combine(Folder, EntityFile)), EntityId.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("""[{"op":"replace","path":"/id","value":"00000000-0000-0000-0000-000000000009"}]""")]
    [InlineData("""[{"op":"replace","path":"/id","value":"D9CCBF89-5004-4D52-8885-2EE377B7CAA5"}]""")]
    [InlineData("""[{"op":"replace","path":"/kind","value":"policy"}]""")]
    [InlineData("""[{"op":"remove","path":"/id"}]""")]
    [InlineData("""[{"op":"remove","path":"/kind"}]""")]
    [InlineData("""[{"op":"replace","path":"","value":[]}]""")]
    [InlineData("""[{"op":"replace","path":"/missing","value":1}]""")]
    [InlineData("""[{"op":"bogus","path":"/x"}]""")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task Patch_that_changes_id_or_kind_or_is_not_a_valid_patch_gets_400_and_leaves_the_file_unchanged(string patch)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        var before = File.ReadAllBytes(path);

        var response = await Patch(client, patch, Sha256(path));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Patch_that_breaks_a_reference_is_saved_and_the_answer_lists_the_diagnostic()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);

        var response = await Patch(client, """[{"op":"replace","path":"/fields/3/target","value":"Missing"}]""", Sha256(path));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Missing", JsonNode.Parse(File.ReadAllBytes(path))!["fields"]![3]!["target"]!.GetValue<string>());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var diagnostic = body.RootElement.GetProperty("diagnostics").EnumerateArray()
            .Single(item => item.GetProperty("code").GetString() == "AXC0012" && item.GetProperty("path").GetString() == "/fields/3/target");
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal(EntityFile, diagnostic.GetProperty("file").GetString());
        Assert.Equal(
            ["code", "file", "message", "path", "resourceId", "severity"],
            diagnostic.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var resourceId = diagnostic.GetProperty("resourceId");
        Assert.True(resourceId.ValueKind == JsonValueKind.Null || string.Equals(resourceId.GetString(), resourceId.GetString()!.ToLowerInvariant(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Patch_without_If_Match_gets_428_and_with_another_content_type_gets_415()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, EntityFile);
        var before = File.ReadAllBytes(path);
        const string patch = """[{"op":"replace","path":"/displayField","value":"number"}]""";

        var missing = await Patch(client, patch, ifMatch: null);
        var wrongType = await Patch(client, patch, Sha256(path), contentType: "application/json");

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Patch_without_the_CSRF_header_or_the_author_role_or_a_session_is_refused_and_the_file_is_unchanged()
    {
        await using var factory = CreateFactory();
        using var hoa = factory.CreateClient().SignedInAs("hoa");
        using var anna = factory.CreateClient().SignedInAs("anna");
        using var anonymous = factory.CreateClient();
        var path = Path.Combine(Folder, EntityFile);
        var before = File.ReadAllBytes(path);
        const string patch = """[{"op":"replace","path":"/displayField","value":"number"}]""";
        var version = Sha256(path);

        var noCsrf = await Patch(hoa, patch, version, csrf: false);
        var forbidden = await Patch(anna, patch, version);
        var unauthorized = await Patch(anonymous, patch, version);

        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("Unknown", EntityId)]
    [InlineData("PurchaseRequests", "00000000-0000-0000-0000-000000000001")]
    [InlineData("PurchaseRequests", "application.json")]
    public async Task Patch_of_an_unknown_application_or_resource_gets_404(string application, string id)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Patch(client, "[]", "x", id, application);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_does_not_write_through_a_link()
    {
        var path = Path.Combine(Folder, EntityFile);
        var outsideFile = Path.Combine(Outside, "purchase-request.json");
        File.Move(path, outsideFile);
        File.CreateSymbolicLink(path, outsideFile);
        var before = File.ReadAllBytes(outsideFile);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Patch(client, """[{"op":"replace","path":"/displayField","value":"number"}]""", Sha256(outsideFile));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(outsideFile));
        Assert.True(new FileInfo(path).LinkTarget is not null);
    }

    [Fact]
    public async Task Two_patches_with_one_version_sent_together_save_only_one()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var version = Sha256(Path.Combine(Folder, EntityFile));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            Patch(client, $$"""[{"op":"replace","path":"/label","value":"Label {{index}}"}]""", version)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Create_writes_the_entity_in_its_kind_folder_in_the_canonical_format()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var id = Guid.NewGuid();
        var body = $$"""{"id":"{{id}}","kind":"entity","name":"CostCentre","formatVersion":1,"fields":[{"name":"code","type":"string"}]}""";

        var response = await Create(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var path = Path.Combine(Folder, "entities", "cost-centre.json");
        Assert.True(File.Exists(path));
        var version = Sha256(path);
        Assert.Equal($"\"{version}\"", response.Headers.ETag?.Tag);
        Assert.Equal($"/api/studio/apps/PurchaseRequests/resources/{id}", response.Headers.Location?.OriginalString);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var root = answer.RootElement;
        Assert.Equal(id.ToString(), root.GetProperty("id").GetString());
        Assert.Equal("entity", root.GetProperty("kind").GetString());
        Assert.Equal("CostCentre", root.GetProperty("name").GetString());
        Assert.Equal("entities/cost-centre.json", root.GetProperty("file").GetString());
        Assert.Equal(version, root.GetProperty("version").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("diagnostics").ValueKind);
        var text = File.ReadAllText(path);
        Assert.Equal(["id", "kind", "name", "formatVersion", "fields"], JsonNode.Parse(text)!.AsObject().Select(pair => pair.Key));
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.StartsWith("  \"id\"", text.Split('\n')[1], StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(Folder, ".*.tmp", SearchOption.AllDirectories));

        var read = await client.GetAsync(new Uri($"/api/studio/apps/PurchaseRequests/resources/{id}", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Theory]
    [InlineData("dataSource", "RequestTotals", null, "data-sources/request-totals.json")]
    [InlineData("policy", "PurchasePolicy", null, "policies/purchase-policy.json")]
    [InlineData("process", "HTTPCall", null, "processes/http-call.json")]
    [InlineData("event", "Step2Approval", null, "events/step2-approval.json")]
    [InlineData("text", "German", "pt-BR", "texts/pt-br.json")]
    [InlineData("role", "cost-centre-owner", null, "roles/cost-centre-owner.json")]
    [InlineData("role", "auditor", null, "roles/auditor.json")]
    public async Task Create_puts_each_kind_in_its_plural_folder_and_names_a_text_after_its_locale(string kind, string name, string? locale, string expected)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var content = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["name"] = name };
        if (locale is not null)
        {
            content["locale"] = locale;
        }

        var body = content.ToJsonString();

        var response = await Create(client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(Folder, expected)));
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(expected, answer.RootElement.GetProperty("file").GetString());
    }

    [Fact]
    public async Task Create_creates_a_missing_kind_folder()
    {
        Directory.Delete(Path.Combine(Folder, "policies"), recursive: true);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Create(client, $$"""{"id":"{{Guid.NewGuid()}}","kind":"policy","name":"Fresh"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(Folder, "policies", "fresh.json")));
    }

    [Theory]
    [InlineData(EntityId, "Fresh")]
    [InlineData("", "PurchaseRequest")]
    public async Task Create_with_an_existing_id_or_target_file_gets_409_and_changes_no_file(string id, string name)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var before = Snapshot();
        var body = $$"""{"id":"{{(id.Length == 0 ? Guid.NewGuid().ToString() : id)}}","kind":"entity","name":"{{name}}"}""";

        var response = await Create(client, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, Snapshot());
    }

    [Theory]
    [InlineData("""{"id":"{0}","kind":"entity","name":"../x"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"x/y"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"x\\y"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"a.b"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":".."}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":""}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"1abc"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"abc\n"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"id":"{0}","kind":"entity"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":5}""")]
    [InlineData("""{"kind":"entity","name":"Fresh"}""")]
    [InlineData("""{"id":"not-a-guid","kind":"entity","name":"Fresh"}""")]
    [InlineData("""{"id":"{0}","name":"Fresh"}""")]
    [InlineData("""{"id":"{0}","kind":"widget","name":"Fresh"}""")]
    [InlineData("""{"id":"{0}","kind":"Entity","name":"Fresh"}""")]
    [InlineData("""{"id":"{0}","kind":"application","name":"Fresh"}""")]
    [InlineData("""{"id":"{0}","kind":"role","name":"../x"}""")]
    [InlineData("""{"id":"{0}","kind":"role","name":"Manager"}""")]
    [InlineData("""{"id":"{0}","kind":"role","name":"-manager"}""")]
    [InlineData("""{"id":"{0}","kind":"role","name":"a.b"}""")]
    [InlineData("""{"id":"{0}","kind":"role","name":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    [InlineData("""{"id":"{0}","kind":"entity","name":"cost-centre"}""")]
    [InlineData("""{"id":"{0}","kind":"text","name":"Fresh","locale":"../x"}""")]
    [InlineData("""{"id":"{0}","kind":"text","name":"Fresh"}""")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("not json")]
    public async Task Create_with_a_bad_body_or_an_unsafe_name_gets_400_and_writes_nothing(string template)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var before = Snapshot();

        var response = await Create(client, template.Replace("{0}", Guid.NewGuid().ToString(), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Create_with_another_content_type_gets_415_and_writes_nothing()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var before = Snapshot();

        var response = await Create(client, $$"""{"id":"{{Guid.NewGuid()}}","kind":"entity","name":"Fresh"}""", "application/json-patch+json");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Create_does_not_write_through_a_linked_kind_folder()
    {
        Directory.Delete(Path.Combine(Folder, "policies"), recursive: true);
        Directory.CreateSymbolicLink(Path.Combine(Folder, "policies"), Outside);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Create(client, $$"""{"id":"{{Guid.NewGuid()}}","kind":"policy","name":"Fresh"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Outside));
    }

    [Fact]
    public async Task Create_and_delete_without_the_CSRF_header_or_the_author_role_or_a_session_are_refused_and_write_nothing()
    {
        await using var factory = CreateFactory();
        using var hoa = factory.CreateClient().SignedInAs("hoa");
        using var anna = factory.CreateClient().SignedInAs("anna");
        using var anonymous = factory.CreateClient();
        var before = Snapshot();
        var body = $$"""{"id":"{{Guid.NewGuid()}}","kind":"entity","name":"Fresh"}""";
        var version = Sha256(Path.Combine(Folder, "entities", "supplier.json"));
        var supplier = SupplierId();

        var created = new[]
        {
            await Create(hoa, body, csrf: false),
            await Create(anna, body),
            await Create(anonymous, body),
        };
        var deleted = new[]
        {
            await Delete(hoa, supplier, version, csrf: false),
            await Delete(anna, supplier, version),
            await Delete(anonymous, supplier, version),
        };

        foreach (var responses in new[] { created, deleted })
        {
            Assert.Equal(
                [HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized],
                responses.Select(response => response.StatusCode));
        }

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task Delete_with_the_current_version_removes_the_file_and_lists_the_references_it_broke()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, "entities", "supplier.json");

        var response = await Delete(client, SupplierId(), $"\"{Sha256(path)}\"");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(File.Exists(path));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var diagnostic = body.RootElement.GetProperty("diagnostics").EnumerateArray()
            .First(item => item.GetProperty("code").GetString() == "AXC0012" && item.GetProperty("file").GetString() == EntityFile);
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("*")]
    public async Task Delete_with_a_stale_or_wildcard_version_gets_409_and_keeps_the_file(string kind)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var path = Path.Combine(Folder, "entities", "supplier.json");
        var old = Sha256(path);
        var supplier = SupplierId();
        var patch = await Patch(client, """[{"op":"replace","path":"/label","value":"Changed"}]""", old, supplier);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var before = File.ReadAllBytes(path);

        var response = await Delete(client, supplier, kind == "*" ? "*" : old);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Delete_without_If_Match_gets_428_and_of_the_manifest_gets_400_and_of_an_unknown_id_gets_404()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var supplier = Path.Combine(Folder, "entities", "supplier.json");
        var manifest = Path.Combine(Folder, "application.json");
        var manifestId = JsonNode.Parse(File.ReadAllBytes(manifest))!["id"]!.GetValue<string>();

        var missing = await Delete(client, SupplierId(), ifMatch: null);
        var refused = await Delete(client, manifestId, Sha256(manifest));
        var unknown = await Delete(client, Guid.NewGuid().ToString(), "x");
        var unknownApplication = await Delete(client, SupplierId(), Sha256(supplier), "Unknown");

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownApplication.StatusCode);
        Assert.True(File.Exists(supplier));
        Assert.True(File.Exists(manifest));
    }

    [Fact]
    public async Task Delete_does_not_remove_a_file_through_a_link()
    {
        var path = Path.Combine(Folder, "entities", "supplier.json");
        var outsideFile = Path.Combine(Outside, "supplier.json");
        var supplier = SupplierId();
        File.Move(path, outsideFile);
        File.CreateSymbolicLink(path, outsideFile);
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Delete(client, supplier, Sha256(outsideFile));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(File.Exists(outsideFile));
    }

    private static async Task<HttpResponseMessage> Patch(
        HttpClient client,
        string patch,
        string? ifMatch,
        string id = EntityId,
        string application = "PurchaseRequests",
        string contentType = "application/json-patch+json",
        bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri($"/api/studio/apps/{application}/resources/{id}", UriKind.Relative))
        {
            Content = new StringContent(patch, Encoding.UTF8, contentType),
        };
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> Create(
        HttpClient client,
        string body,
        string contentType = "application/json",
        bool csrf = true,
        string application = "PurchaseRequests")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/studio/apps/{application}/resources", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> Delete(
        HttpClient client,
        string id,
        string? ifMatch,
        string application = "PurchaseRequests",
        bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/studio/apps/{application}/resources/{id}", UriKind.Relative));
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private string SupplierId() =>
        JsonNode.Parse(File.ReadAllBytes(Path.Combine(Folder, "entities", "supplier.json")))!["id"]!.GetValue<string>();

    // Every file and folder under the test root, which includes the folder outside the application, with its bytes.
    private List<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path) + (File.Exists(path) ? ":" + Sha256(path) : "/"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private WebApplicationFactory<Program> CreateFactory(string? folder = null, string authorRole = "author", string? modulesPath = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            if (modulesPath is not null)
            {
                builder.UseSetting("Modules:Path", modulesPath);
            }

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
