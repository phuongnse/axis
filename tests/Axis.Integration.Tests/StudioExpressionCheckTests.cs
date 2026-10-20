using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Json.Pointer;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The expression check route puts an expression into a copy of the saved resource and compiles the folder in memory. It
/// answers the type or the first problem with its range, and it writes nothing.
/// </summary>
public sealed class StudioExpressionCheckTests : IClassFixture<StudioExpressionCheckTests.StudioFixture>
{
    private const string Host = StudioPublishTests.HostA;

    private const string CheckUrl = "/api/studio/apps/PurchaseRequests/expressions/check";

    private const string MigrationFile = "migrations/0001-fill-title.json";

    private readonly StudioFixture _studio;

    public StudioExpressionCheckTests(StudioFixture studio) => _studio = studio;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> Places => new()
    {
        { "entities/purchase-request.json", "/fields/7/expression", "decimal" },
        { "entities/purchase-request.json", "/validations/0/expression", "boolean" },
        { "rules/finance-threshold.json", "/expression", "integer" },
        { "data-sources/purchase-request-list.json", "/filter", "boolean" },
        { "processes/purchase-request-approval.json", "/startCondition/expression", "boolean" },
        { "processes/purchase-request-approval.json", "/steps/2/branches/0/when", "boolean" },
        { "processes/purchase-request-approval.json", "/steps/0/set/status", "text" },
        { "processes/purchase-request-approval.json", "/steps/1/assignee/user", "text" },
        { "policies/department-manager-access.json", "/grants/0/filter", "boolean" },
        { "endpoints/submit-purchase-request.json", "/values/status", "text" },
        { MigrationFile, "/changes/0/value", "text" },
    };

    [Theory]
    [MemberData(nameof(Places))]
    public async Task A_saved_expression_answers_its_type_and_no_problem_where_a_save_shows_none(string file, string path, string type)
    {
        var saved = ReadString(file, path);

        using var answer = await _studio.CheckAsync(await _studio.IdOfAsync(file), path, saved);

        Assert.Equal(type, answer.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, answer.RootElement.GetProperty("diagnostics").GetArrayLength());
        using var diagnostics = await _studio.GetJsonAsync("/api/studio/apps/PurchaseRequests/diagnostics");
        Assert.DoesNotContain(
            diagnostics.RootElement.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("file").GetString() == file && item.GetProperty("path").GetString() == path);
    }

    [Fact]
    public async Task A_type_error_answers_the_compiles_code_with_the_range_of_the_faulty_part_and_writes_nothing()
    {
        const string File = "entities/line-item.json";
        var before = Hash(File);
        var versionBefore = await _studio.VersionOfAsync(File);

        using var answer = await _studio.CheckAsync(await _studio.IdOfAsync(File), "/validations/0/expression", "quantity > 'x'");

        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("type").ValueKind);
        var diagnostic = Assert.Single(answer.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AXC0047", diagnostic.GetProperty("code").GetString());
        Assert.Equal(9, diagnostic.GetProperty("start").GetInt32());
        Assert.Equal(10, diagnostic.GetProperty("end").GetInt32());
        Assert.Equal(before, Hash(File));
        Assert.Equal(versionBefore, await _studio.VersionOfAsync(File));
    }

    [Fact]
    public async Task A_syntax_error_covers_the_bad_character_and_a_result_of_the_wrong_type_covers_the_whole_expression()
    {
        var id = await _studio.IdOfAsync("entities/line-item.json");

        using var syntax = await _studio.CheckAsync(id, "/validations/0/expression", "quantity # 0");
        var bad = Assert.Single(syntax.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AXC0035", bad.GetProperty("code").GetString());
        Assert.Equal((9, 10), (bad.GetProperty("start").GetInt32(), bad.GetProperty("end").GetInt32()));

        using var wrong = await _studio.CheckAsync(id, "/validations/0/expression", "quantity + 1");
        var mismatch = Assert.Single(wrong.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AXC0048", mismatch.GetProperty("code").GetString());
        Assert.Equal((0, 12), (mismatch.GetProperty("start").GetInt32(), mismatch.GetProperty("end").GetInt32()));
    }

    [Fact]
    public async Task A_field_name_answers_the_enum_type_where_a_text_literal_answers_text()
    {
        using var answer = await _studio.CheckAsync(
            await _studio.IdOfAsync("processes/purchase-request-approval.json"), "/steps/0/set/status", "status");

        Assert.Equal("enum", answer.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, answer.RootElement.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task A_path_that_holds_no_expression_answers_400_and_an_unknown_resource_answers_404()
    {
        var id = await _studio.IdOfAsync("entities/line-item.json");

        foreach (var path in new[] { "/name", "/validations/0", "/nothing/here", "" })
        {
            using var response = await StudioFixture.SendAsync(_studio.Author, CheckBody(id, path, "quantity > 0"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var unknown = await StudioFixture.SendAsync(_studio.Author, CheckBody(Guid.NewGuid().ToString("D"), "/validations/0/expression", "quantity > 0"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var notAnId = await StudioFixture.SendAsync(_studio.Author, CheckBody("nope", "/validations/0/expression", "quantity > 0"));
        Assert.Equal(HttpStatusCode.BadRequest, notAnId.StatusCode);

        using var wrongType = await StudioFixture.SendAsync(_studio.Author, """{ "resourceId": "x", "path": "/name", "expression": 1 }""", "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);
    }

    [Fact]
    public async Task The_route_needs_a_session_the_author_role_and_the_csrf_header()
    {
        var id = await _studio.IdOfAsync("entities/line-item.json");
        var body = CheckBody(id, "/validations/0/expression", "quantity > 0");

        using var signedOut = await StudioFixture.SendAsync(_studio.Anonymous, body);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);

        using var employee = await StudioFixture.SendAsync(_studio.Employee, body);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        using var withoutCsrf = new HttpRequestMessage(HttpMethod.Post, new Uri(CheckUrl, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        withoutCsrf.Headers.Host = Host;
        using var noHeader = await _studio.Author.SendAsync(withoutCsrf, CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);

        using var unknownApplication = await StudioFixture.SendAsync(_studio.Author, body, url: "/api/studio/apps/NoSuchApplication/expressions/check");
        Assert.Equal(HttpStatusCode.NotFound, unknownApplication.StatusCode);
    }

    private static string CheckBody(string id, string path, string expression) =>
        new JsonObject { ["resourceId"] = id, ["path"] = path, ["expression"] = expression }.ToJsonString();

    private string ReadString(string file, string path)
    {
        Assert.True(JsonPointer.Parse(path).TryEvaluate(JsonNode.Parse(File.ReadAllText(Path.Combine(_studio.Folder, file))), out var value));
        return (string)value!;
    }

    private string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_studio.Folder, file))));

    public sealed class StudioFixture : IAsyncLifetime
    {
        private const string MigrationId = "7b3096e8-c14a-48f2-9a56-30bbd774e102";

        private readonly PostgreSqlFixture _database = new();
        private TemporaryFolder? _folder;
        private WebApplicationFactory<Program>? _factory;

        public string Folder => _folder!.Path;

        public HttpClient Author { get; private set; } = null!;

        public HttpClient Employee { get; private set; } = null!;

        public HttpClient Anonymous { get; private set; } = null!;

        public async ValueTask InitializeAsync()
        {
            await _database.InitializeAsync();
            _folder = StudioPublishTests.NewFolder();
            var connectionString = await _database.CreateDatabaseAsync("studio_expression_check", CancellationToken);
            _factory = StudioPublishTests.NewFactory(_folder.Path, [("a", Host, connectionString)]);
            Author = _factory.CreateClient().SignedInAs("hoa");
            Employee = _factory.CreateClient().SignedInAs("anna");
            Anonymous = _factory.CreateClient();

            // The server has activated the folder by now. The migration only has to compile, so it is written afterwards.
            Directory.CreateDirectory(Path.Combine(_folder.Path, "migrations"));
            await File.WriteAllTextAsync(
                Path.Combine(_folder.Path, MigrationFile),
                $$"""
                {
                  "id": "{{MigrationId}}",
                  "kind": "migration",
                  "name": "FillTitle",
                  "formatVersion": 1,
                  "entity": "PurchaseRequest",
                  "changes": [ { "type": "fill", "field": "title", "value": "'untitled'" } ]
                }
                """,
                CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Author?.Dispose();
            Employee?.Dispose();
            Anonymous?.Dispose();
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }

            _folder?.Dispose();
            await _database.DisposeAsync();
        }

        public async Task<string> IdOfAsync(string file)
        {
            using var list = await GetJsonAsync("/api/studio/apps/PurchaseRequests/resources");
            return list.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("file").GetString() == file).GetProperty("id").GetString()!;
        }

        public async Task<string> VersionOfAsync(string file)
        {
            using var list = await GetJsonAsync("/api/studio/apps/PurchaseRequests/resources");
            return list.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("file").GetString() == file).GetProperty("version").GetString()!;
        }

        public async Task<JsonDocument> CheckAsync(string id, string path, string expression)
        {
            using var response = await SendAsync(Author, CheckBody(id, path, expression));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            using var request = RecordApiFixture.Request(HttpMethod.Get, path, Host);
            using var response = await Author.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }

        public static async Task<HttpResponseMessage> SendAsync(
            HttpClient client, string body, string contentType = "application/json", string url = CheckUrl)
        {
            using var request = RecordApiFixture.Request(HttpMethod.Post, url, Host, body, contentType);
            return await client.SendAsync(request, CancellationToken);
        }
    }
}
