using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Presentation.Sites;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Axis.Server.Tests;

public sealed class StudioPageMetadataTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. The route reads no record, so it must answer with this database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private const string PageFile = "pages/purchase-requests.json";

    private const string PageId = "e0ee63b0-ff11-4f06-95ee-e73922f2c1a9";

    private const string EntityId = "d9ccbf89-5004-4d52-8885-2ee377b7caa5";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"axis-studio-page-{Guid.NewGuid():N}");

    private string Folder => Path.Combine(_root, "purchase-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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
    public async Task Valid_draft_answers_every_widget_with_its_path_and_full_access_for_an_author_with_no_application_role()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Post(client, TwoColumns(ValidChart()).ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var page = body.RootElement.GetProperty("page");
        Assert.Equal("PurchaseRequests", page.GetProperty("name").GetString());
        Assert.Empty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
        var columns = page.GetProperty("widgets")[0];
        Assert.Equal("columns", columns.GetProperty("type").GetString());
        Assert.Equal("/widgets/0", columns.GetProperty("path").GetString());
        var table = columns.GetProperty("columns")[0].GetProperty("widgets")[0];
        Assert.Equal("table", table.GetProperty("type").GetString());
        Assert.Equal("/widgets/0/columns/0/widgets/0", table.GetProperty("path").GetString());
        Assert.Equal(
            ["read", "create", "update", "delete"],
            table.GetProperty("access").GetProperty("actions").EnumerateArray().Select(action => action.GetString()).ToArray());
        var chart = columns.GetProperty("columns")[1].GetProperty("widgets")[0];
        Assert.Equal("chart", chart.GetProperty("type").GetString());
        Assert.Equal("/widgets/0/columns/1/widgets/0", chart.GetProperty("path").GetString());
        Assert.False(chart.TryGetProperty("placeholder", out _));
        Assert.Equal(4, columns.GetProperty("columns")[1].GetProperty("width").GetInt32());
    }

    [Fact]
    public async Task Unchanged_page_answers_its_widget_at_the_top_level()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Post(client, SavedPage().ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("/widgets/0", body.RootElement.GetProperty("page").GetProperty("widgets")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Widget_metadata_writes_no_path_until_the_draft_route_sets_one()
    {
        // The site's page route shares these types, and its answer must stay as it was.
        var widget = new ColumnsWidgetMetadata([new ColumnMetadata(12, [])]);

        Assert.Equal("""{"type":"columns","columns":[{"width":12,"widgets":[]}]}""", JsonSerializer.Serialize<PageWidgetMetadata>(widget, JsonSerializerOptions.Web));
        Assert.Equal("""{"type":"columns","path":"/widgets/0","columns":[{"width":12,"widgets":[]}]}""", JsonSerializer.Serialize<PageWidgetMetadata>(widget with { Path = "/widgets/0" }, JsonSerializerOptions.Web));
    }

    [Fact]
    public async Task Widget_that_does_not_compile_is_a_placeholder_and_the_rest_of_the_page_is_answered_in_full()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Post(client, TwoColumns(BrokenChart()).ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var columns = body.RootElement.GetProperty("page").GetProperty("widgets")[0].GetProperty("columns");
        var table = columns[0].GetProperty("widgets")[0];
        Assert.Equal("table", table.GetProperty("type").GetString());
        Assert.Equal("/widgets/0/columns/0/widgets/0", table.GetProperty("path").GetString());
        Assert.Equal("PurchaseRequestList", table.GetProperty("dataSource").GetProperty("name").GetString());
        Assert.Equal(8, columns[0].GetProperty("width").GetInt32());
        Assert.Equal(4, columns[1].GetProperty("width").GetInt32());
        var placeholder = columns[1].GetProperty("widgets")[0];
        Assert.Equal(3, placeholder.EnumerateObject().Count());
        Assert.Equal("chart", placeholder.GetProperty("type").GetString());
        Assert.Equal("/widgets/0/columns/1/widgets/0", placeholder.GetProperty("path").GetString());
        Assert.True(placeholder.GetProperty("placeholder").GetBoolean());

        var diagnostics = body.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();
        Assert.Contains(diagnostics, item => item.GetProperty("path").GetString()!.StartsWith("/widgets/0/columns/1/widgets/0", StringComparison.Ordinal));
        Assert.All(diagnostics, item => Assert.Equal(PageId, item.GetProperty("resourceId").GetString()));
    }

    [Fact]
    public async Task Widget_with_a_schema_error_is_a_placeholder_and_the_rest_of_the_page_is_answered()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var chart = ValidChart();
        chart["unknown"] = true;

        var response = await Post(client, TwoColumns(chart).ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var columns = body.RootElement.GetProperty("page").GetProperty("widgets")[0].GetProperty("columns");
        Assert.Equal("table", columns[0].GetProperty("widgets")[0].GetProperty("type").GetString());
        var placeholder = columns[1].GetProperty("widgets")[0];
        Assert.True(placeholder.GetProperty("placeholder").GetBoolean());
        Assert.Equal("/widgets/0/columns/1/widgets/0", placeholder.GetProperty("path").GetString());
        Assert.NotEmpty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Placeholder_type_is_null_when_the_draft_type_is_not_a_string()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var broken = new JsonObject { ["type"] = 7 };

        var response = await Post(client, TwoColumns(broken).ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var placeholder = body.RootElement.GetProperty("page").GetProperty("widgets")[0].GetProperty("columns")[1].GetProperty("widgets")[0];
        Assert.Equal(JsonValueKind.Null, placeholder.GetProperty("type").ValueKind);
        Assert.True(placeholder.GetProperty("placeholder").GetBoolean());
    }

    [Fact]
    public async Task Container_whose_widgets_all_fail_is_one_placeholder()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var page = SavedPage();
        page["widgets"] = new JsonArray(
            new JsonObject
            {
                ["type"] = "section",
                ["title"] = new JsonObject { ["textKey"] = "pages.purchaseRequests.title" },
                ["widgets"] = new JsonArray(BrokenChart()),
            },
            SavedPage()["widgets"]![0]!.DeepClone());

        var response = await Post(client, page.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var widgets = body.RootElement.GetProperty("page").GetProperty("widgets");
        Assert.Equal("section", widgets[0].GetProperty("type").GetString());
        Assert.Equal("/widgets/0", widgets[0].GetProperty("path").GetString());
        Assert.True(widgets[0].GetProperty("placeholder").GetBoolean());
        Assert.Equal("table", widgets[1].GetProperty("type").GetString());
        Assert.Equal("/widgets/1", widgets[1].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Page_is_null_when_no_top_level_widget_compiles()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var page = SavedPage();
        page["widgets"] = new JsonArray(BrokenChart());

        var response = await Post(client, page.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("page").ValueKind);
        Assert.NotEmpty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Page_is_null_when_the_page_has_an_error_outside_its_widgets()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var page = SavedPage();
        page.Remove("title");

        var response = await Post(client, page.ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("page").ValueKind);
        Assert.NotEmpty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Page_is_null_when_another_file_has_an_error_and_its_diagnostics_are_not_listed()
    {
        File.WriteAllText(Path.Combine(Folder, "pages", "suppliers.json"), """{ "id": "not-an-id" }""");
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");

        var response = await Post(client, SavedPage().ToJsonString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("page").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public async Task Refused_callers_get_the_status_of_the_other_authoring_routes()
    {
        await using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var anna = factory.CreateClient().SignedInAs("anna");
        using var hoa = factory.CreateClient().SignedInAs("hoa");
        var json = SavedPage().ToJsonString();

        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonymous, json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(anna, json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(hoa, json, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(hoa, json, application: "Unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(hoa, json, id: Guid.NewGuid().ToString("D"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(hoa, json, id: "not-a-guid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(hoa, json, id: EntityId)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Post(hoa, json, contentType: "text/plain")).StatusCode);

        var otherId = SavedPage();
        otherId["id"] = Guid.NewGuid().ToString("D");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(hoa, otherId.ToJsonString())).StatusCode);

        var upperId = SavedPage();
        upperId["id"] = PageId.ToUpperInvariant();
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(hoa, upperId.ToJsonString())).StatusCode);

        var otherKind = SavedPage();
        otherKind["kind"] = "entity";
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(hoa, otherKind.ToJsonString())).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(hoa, "[]")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(hoa, "{")).StatusCode);
    }

    [Fact]
    public async Task Route_writes_no_file_and_reads_no_record_while_the_tenant_database_is_unreachable()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient().SignedInAs("hoa");
        var before = Snapshot();

        // Every database connection is refused, so each 200 shows that no record was read.
        Assert.Equal(HttpStatusCode.OK, (await Post(client, SavedPage().ToJsonString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, TwoColumns(ValidChart()).ToJsonString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, TwoColumns(BrokenChart()).ToJsonString())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "[]")).StatusCode);

        Assert.Equal(before, Snapshot());
    }

    private static JsonObject ValidChart() => new()
    {
        ["type"] = "chart",
        ["kind"] = "bar",
        ["dataSource"] = "RequestsByDepartment",
        ["category"] = "department",
        ["measures"] = new JsonArray(new JsonObject
        {
            ["name"] = "requests",
            ["label"] = new JsonObject { ["textKey"] = "pages.purchaseRequests.title" },
        }),
        ["title"] = new JsonObject { ["textKey"] = "pages.purchaseRequests.title" },
    };

    private static JsonObject BrokenChart()
    {
        var chart = ValidChart();
        chart["dataSource"] = "NoSuchSource";
        return chart;
    }

    private JsonObject SavedPage() => (JsonObject)JsonNode.Parse(File.ReadAllBytes(Path.Combine(Folder, PageFile)))!;

    // The saved table in a column of 8, and the given widget in a column of 4.
    private JsonObject TwoColumns(JsonObject second)
    {
        var page = SavedPage();
        page["widgets"] = new JsonArray(new JsonObject
        {
            ["type"] = "columns",
            ["columns"] = new JsonArray(
                new JsonObject { ["width"] = 8, ["widgets"] = new JsonArray(page["widgets"]![0]!.DeepClone()) },
                new JsonObject { ["width"] = 4, ["widgets"] = new JsonArray(second) }),
        });
        return page;
    }

    private static async Task<HttpResponseMessage> Post(
        HttpClient client,
        string body,
        string application = "PurchaseRequests",
        string id = PageId,
        string contentType = "application/json",
        bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/studio/apps/{application}/resources/{id}/page-metadata", UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    // Every file and folder under the test root, with its bytes.
    private List<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path) + (File.Exists(path) ? ":" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "/"))
            .Order(StringComparer.Ordinal)
            .ToList();

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
            builder.UseTestAuthentication(
                new TestIdentity("hoa", "Hoa Author", "author"),
                new TestIdentity("anna", "Anna Employee", "employee"));
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
