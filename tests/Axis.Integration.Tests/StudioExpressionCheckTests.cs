using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Tests;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Functions;
using Axis.Server.Tests;
using Json.Pointer;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// The expression check route puts an expression into a copy of the saved resource and compiles the folder in memory. It
/// answers the type or the first problem with its range, and it writes nothing. The scope route answers what a place offers:
/// the type it needs, the fields in scope and the functions it allows, from the same compile.
/// </summary>
public sealed class StudioExpressionCheckTests : IClassFixture<StudioExpressionCheckTests.StudioFixture>
{
    private const string Host = StudioPublishTests.HostA;

    private const string CheckUrl = "/api/studio/apps/PurchaseRequests/expressions/check";

    private const string ScopeUrl = "/api/studio/apps/PurchaseRequests/expressions/scope";

    private const string MigrationFile = "migrations/0001-fill-title.json";

    // Its entity is dropped and not in the release, so the field types are unknown and the compile type checks no value.
    private const string UntypedMigrationFile = "migrations/0002-retired-thing.json";

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
    public async Task A_default_has_no_record_in_scope_so_a_field_name_answers_the_unknown_name_code_and_a_literal_answers_its_type()
    {
        const string File = "entities/purchase-request.json";
        // The title field is text and has no expression, sequence or creator, and it has no default in the file.
        const string Path = "/fields/1/default";
        var id = await _studio.IdOfAsync(File);
        var before = Hash(File);

        using var name = await _studio.CheckAsync(id, Path, "requester");
        Assert.Equal(JsonValueKind.Null, name.RootElement.GetProperty("type").ValueKind);
        var unknown = Assert.Single(name.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(ExpressionDiagnosticCodes.UnknownName, unknown.GetProperty("code").GetString());

        using var literal = await _studio.CheckAsync(id, Path, "'untitled'");
        Assert.Equal("text", literal.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, literal.RootElement.GetProperty("diagnostics").GetArrayLength());

        Assert.Equal(before, Hash(File));
    }

    [Fact]
    public async Task A_condition_not_yet_saved_is_checked_where_the_form_field_would_hold_it()
    {
        const string File = "forms/purchase-request-review.json";
        const string Path = "/sections/1/fields/0/requiredWhen";
        var id = await _studio.IdOfAsync(File);
        var before = Hash(File);

        using var boolean = await _studio.CheckAsync(id, Path, "totalAmount > 1000");
        Assert.Equal("boolean", boolean.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, boolean.RootElement.GetProperty("diagnostics").GetArrayLength());

        using var wrong = await _studio.CheckAsync(id, Path, "totalAmount + 1");
        Assert.Equal(JsonValueKind.Null, wrong.RootElement.GetProperty("type").ValueKind);
        Assert.Single(wrong.RootElement.GetProperty("diagnostics").EnumerateArray());

        Assert.Equal(before, Hash(File));
    }

    [Fact]
    public async Task A_condition_that_reads_a_field_the_form_does_not_list_answers_the_unlisted_field_code()
    {
        var id = await _studio.IdOfAsync("forms/purchase-request-review.json");

        // The review form does not list purchaseOrderNumber.
        using var answer = await _studio.CheckAsync(id, "/sections/1/fields/0/requiredWhen", "purchaseOrderNumber is not null");

        var diagnostic = Assert.Single(answer.RootElement.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(DiagnosticCodes.UnlistedFormConditionField, diagnostic.GetProperty("code").GetString());
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
    public async Task A_validation_not_yet_saved_is_checked_in_the_draft_and_answers_400_without_it()
    {
        const string SupplierFile = "entities/supplier.json";
        const string ValidationPath = "/validations/0/expression";
        var id = await _studio.IdOfAsync(SupplierFile);
        var before = Hash(SupplierFile);

        using var withoutDraft = await StudioFixture.SendAsync(_studio.Author, CheckBody(id, ValidationPath, "code is not null"));
        Assert.Equal(HttpStatusCode.BadRequest, withoutDraft.StatusCode);

        var draft = JsonNode.Parse(File.ReadAllText(Path.Combine(_studio.Folder, SupplierFile)))!.AsObject();
        draft["validations"] = new JsonArray(new JsonObject
        {
            ["name"] = "codeSet",
            ["expression"] = string.Empty,
            ["field"] = "code",
            ["message"] = new JsonObject { ["textKey"] = "supplier.code" },
        });
        using var answer = await _studio.CheckAsync(id, ValidationPath, "code is not null", draft);

        Assert.Equal("boolean", answer.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, answer.RootElement.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(before, Hash(SupplierFile));
    }

    [Fact]
    public async Task A_draft_that_is_not_an_object_answers_400()
    {
        var id = await _studio.IdOfAsync("entities/line-item.json");

        using var response = await StudioFixture.SendAsync(
            _studio.Author, CheckBody(id, "/validations/0/expression", "quantity > 0", JsonValue.Create("x")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    [Fact]
    public async Task A_field_defaults_scope_has_no_fields_and_no_aggregate_functions()
    {
        const string File = "entities/purchase-request.json";
        var before = Hash(File);

        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync(File), "/fields/1/default");

        Assert.Equal("text", answer.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, answer.RootElement.GetProperty("fields").GetArrayLength());
        var functions = FunctionNames(answer);
        Assert.Contains("now", functions);
        Assert.Empty(functions.Intersect(ExpressionFunctions.AggregateNames));
        Assert.Equal(before, Hash(File));
    }

    [Fact]
    public async Task A_computed_fields_scope_lists_no_current_time_function_but_lists_the_aggregates()
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync("entities/purchase-request.json"), "/fields/7/expression");

        Assert.Equal("decimal", answer.RootElement.GetProperty("type").GetString());
        var functions = FunctionNames(answer);
        Assert.DoesNotContain("now", functions);
        Assert.DoesNotContain("today", functions);
        Assert.DoesNotContain("toDate", functions);
        Assert.Contains("sum", functions);
        Assert.Contains("lower", functions);
    }

    [Fact]
    public async Task A_validations_scope_needs_boolean_and_lists_the_entitys_fields_with_their_label_keys_and_types()
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync("entities/line-item.json"), "/validations/0/expression");

        Assert.Equal("boolean", answer.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            [
                ("description", "lineItem.description", "text"),
                ("quantity", "lineItem.quantity", "integer"),
                ("unitPrice", "lineItem.unitPrice", "decimal"),
                ("amount", "lineItem.amount", "decimal"),
            ],
            answer.RootElement.GetProperty("fields").EnumerateArray().Select(field => (
                field.GetProperty("name").GetString()!,
                field.GetProperty("labelKey").GetString()!,
                field.GetProperty("type").GetString()!)));
    }

    [Fact]
    public async Task A_data_sources_scope_lists_the_entitys_fields_with_label_keys_then_the_parameters_with_none()
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync("data-sources/purchase-request-list.json"), "/filter");

        Assert.Equal("boolean", answer.RootElement.GetProperty("type").GetString());
        var fields = answer.RootElement.GetProperty("fields").EnumerateArray().ToList();
        var status = fields.Single(field => field.GetProperty("name").GetString() == "status");
        Assert.Equal("purchaseRequest.status", status.GetProperty("labelKey").GetString());
        Assert.Equal("enum", status.GetProperty("type").GetString());
        var parameter = fields[^1];
        Assert.Equal("statusFilter", parameter.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, parameter.GetProperty("labelKey").ValueKind);
        Assert.Equal("enum", parameter.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_data_sources_scope_lists_only_the_functions_of_the_SQL_subset()
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync("data-sources/purchase-request-list.json"), "/filter");

        var functions = FunctionNames(answer);
        Assert.DoesNotContain("lower", functions);
        Assert.DoesNotContain("upper", functions);
        Assert.DoesNotContain("trim", functions);
        Assert.DoesNotContain("sum", functions);
        Assert.Contains("any", functions);
        Assert.Contains("count", functions);
        Assert.Contains("currentUser", functions);
    }

    [Theory]
    [InlineData("policies/department-manager-access.json", "/grants/0/filter")]
    [InlineData(MigrationFile, "/changes/0/value")]
    public async Task A_policy_filters_and_a_migration_values_scope_lists_no_function_outside_the_SQL_subset(string file, string path)
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync(file), path);

        var functions = FunctionNames(answer);
        Assert.DoesNotContain("lower", functions);
        Assert.DoesNotContain("upper", functions);
        Assert.DoesNotContain("trim", functions);
        Assert.Contains("length", functions);
    }

    [Fact]
    public async Task A_place_without_a_type_check_answers_no_type_no_fields_and_no_functions()
    {
        using var answer = await _studio.ScopeAsync(await _studio.IdOfAsync(UntypedMigrationFile), "/changes/1/value");

        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("type").ValueKind);
        Assert.Equal(0, answer.RootElement.GetProperty("fields").GetArrayLength());
        Assert.Equal(0, answer.RootElement.GetProperty("functions").GetArrayLength());
    }

    [Fact]
    public async Task The_scope_route_refuses_as_the_check_does_and_writes_nothing()
    {
        const string File = "entities/line-item.json";
        var id = await _studio.IdOfAsync(File);
        var before = Hash(File);
        var versionBefore = await _studio.VersionOfAsync(File);
        var body = ScopeBody(id, "/validations/0/expression");

        using var signedOut = await StudioFixture.SendAsync(_studio.Anonymous, body, url: ScopeUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);

        using var employee = await StudioFixture.SendAsync(_studio.Employee, body, url: ScopeUrl);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        using var unknownApplication = await StudioFixture.SendAsync(
            _studio.Author, body, url: "/api/studio/apps/NoSuchApplication/expressions/scope");
        Assert.Equal(HttpStatusCode.NotFound, unknownApplication.StatusCode);

        using var wrongType = await StudioFixture.SendAsync(_studio.Author, body, "text/plain", ScopeUrl);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongType.StatusCode);

        using var notAnId = await StudioFixture.SendAsync(_studio.Author, ScopeBody("nope", "/validations/0/expression"), url: ScopeUrl);
        Assert.Equal(HttpStatusCode.BadRequest, notAnId.StatusCode);

        using var unknown = await StudioFixture.SendAsync(
            _studio.Author, ScopeBody(Guid.NewGuid().ToString("D"), "/validations/0/expression"), url: ScopeUrl);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var noExpression = await StudioFixture.SendAsync(_studio.Author, ScopeBody(id, "/name"), url: ScopeUrl);
        Assert.Equal(HttpStatusCode.BadRequest, noExpression.StatusCode);

        Assert.Equal(before, Hash(File));
        Assert.Equal(versionBefore, await _studio.VersionOfAsync(File));
    }

    private static List<string> FunctionNames(JsonDocument answer) =>
        [.. answer.RootElement.GetProperty("functions").EnumerateArray().Select(function => function.GetProperty("name").GetString()!)];

    private static string ScopeBody(string id, string path) =>
        new JsonObject { ["resourceId"] = id, ["path"] = path }.ToJsonString();

    private static string CheckBody(string id, string path, string expression, JsonNode? draft = null)
    {
        var body = new JsonObject { ["resourceId"] = id, ["path"] = path, ["expression"] = expression };
        if (draft is not null)
        {
            body["draft"] = draft;
        }

        return body.ToJsonString();
    }

    private string ReadString(string file, string path)
    {
        Assert.True(JsonPointer.Parse(path).TryEvaluate(JsonNode.Parse(File.ReadAllText(Path.Combine(_studio.Folder, file))), out var value));
        return (string)value!;
    }

    private string Hash(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(_studio.Folder, file))));

    public sealed class StudioFixture : IAsyncLifetime
    {
        private const string MigrationId = "7b3096e8-c14a-48f2-9a56-30bbd774e102";

        private const string UntypedMigrationId = "1d0f6f0e-6a7e-4a43-9d5e-5e0a3f1b7c21";

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
            Author = _factory.CreateClient().SignedInAs("author");
            Employee = _factory.CreateClient().SignedInAs("requester");
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
            await File.WriteAllTextAsync(
                Path.Combine(_folder.Path, UntypedMigrationFile),
                $$"""
                {
                  "id": "{{UntypedMigrationId}}",
                  "kind": "migration",
                  "name": "RetiredThing",
                  "formatVersion": 1,
                  "entity": "RetiredThing",
                  "changes": [ { "type": "dropEntity" }, { "type": "fill", "field": "title", "value": "'untitled'" } ]
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

        public async Task<JsonDocument> CheckAsync(string id, string path, string expression, JsonNode? draft = null)
        {
            using var response = await SendAsync(Author, CheckBody(id, path, expression, draft));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }

        public async Task<JsonDocument> ScopeAsync(string id, string path)
        {
            using var response = await SendAsync(Author, ScopeBody(id, path), url: ScopeUrl);
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
