using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordWriteEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Departments = "/api/apps/RecordsApp/entities/Department/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_with_every_field_type_returns_the_record_at_its_canonical_location()
    {
        await fixture.ResetAsync();
        var departmentId = await CreateDepartmentAsync(HostA, "Sales");
        var values = $$"""
            {
              "name": "Desk",
              "quantity": 3,
              "price": 1250.50,
              "active": true,
              "neededBy": "2026-10-06",
              "orderedAt": "2026-10-06T02:00:00.123456Z",
              "status": "open",
              "department": "{{departmentId:D}}"
            }
            """;

        // The path names the application and entity in a different letter case than the model.
        using var request = Request(HttpMethod.Post, "/api/apps/recordsapp/entities/ITEM/records", HostA, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = await ReadJsonAsync(response);
        var id = created.RootElement.GetProperty("id").GetString();
        Assert.Equal($"{Items}/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(Guid.Parse(id!).ToString("D"), id);
        Assert.Equal(1, created.RootElement.GetProperty("version").GetInt64());

        using var read = await GetJsonAsync(response.Headers.Location!.OriginalString, HostA);

        Assert.Equal(id, read.RootElement.GetProperty("id").GetString());
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
        using var expected = JsonDocument.Parse(values);
        Assert.Equal(
            expected.RootElement.EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())),
            read.RootElement.GetProperty("values").EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())));
    }

    [Fact]
    public async Task Update_changes_only_the_named_fields_and_rejects_a_stale_version()
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync(HostA, """{ "name": "Desk", "quantity": 3, "active": true }""");

        using var updated = await PatchJsonAsync(id, """{ "version": 1, "values": { "quantity": 5 } }""", HttpStatusCode.OK);

        Assert.Equal(2, updated.RootElement.GetProperty("version").GetInt64());
        var values = updated.RootElement.GetProperty("values");
        Assert.Equal("\"Desk\"", values.GetProperty("name").GetRawText());
        Assert.Equal("5", values.GetProperty("quantity").GetRawText());
        Assert.Equal("true", values.GetProperty("active").GetRawText());

        using var stale = await PatchJsonAsync(id, """{ "version": 1, "values": { "quantity": 7 } }""", HttpStatusCode.Conflict);

        Assert.Equal("The record has changed since this version was read.", stale.RootElement.GetProperty("title").GetString());

        using var cleared = await PatchJsonAsync(id, """{ "version": 2, "values": { "quantity": null } }""", HttpStatusCode.OK);

        Assert.Equal(3, cleared.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, cleared.RootElement.GetProperty("values").GetProperty("quantity").ValueKind);
        Assert.Equal("\"Desk\"", cleared.RootElement.GetProperty("values").GetProperty("name").GetRawText());

        using var bumped = await PatchJsonAsync(id, """{ "version": 3, "values": {} }""", HttpStatusCode.OK);

        Assert.Equal(4, bumped.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(
            cleared.RootElement.GetProperty("values").GetRawText(),
            bumped.RootElement.GetProperty("values").GetRawText());
    }

    public static TheoryData<bool, string, string> InvalidBodies => new()
    {
        { false, """{ "values": { "name": "Desk", "extra": 1 } }""", "/values/extra" },
        { false, """{ "values": { "quantity": 1 } }""", "/values/name" },
        { false, """{ "values": { "name": "Desk", "quantity": "5" } }""", "/values/quantity" },
        { false, """{ "values": { "name": "Desk", "status": "pending" } }""", "/values/status" },
        { false, $$"""{ "values": { "name": "Desk", "department": "{{Guid.NewGuid()}}" } }""", "/values/department" },
        { false, """{ "values": """, "" },
        { false, """[ { "values": {} } ]""", "" },
        { true, """{ "values": { "quantity": 1 } }""", "/version" },
        { true, $$"""{ "version": 1, "values": { "department": "{{Guid.NewGuid()}}" } }""", "/values/department" },
        { true, "not json", "" },
        { true, "42", "" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_body_is_a_400_problem_keyed_by_json_pointer_and_writes_nothing(bool update, string body, string key)
    {
        await fixture.ResetAsync();
        var existing = update ? await CreateItemAsync(HostA, """{ "name": "Desk" }""") : (string?)null;
        using var request = update
            ? Request(HttpMethod.Patch, $"{Items}/{existing}", HostA, body)
            : Request(HttpMethod.Post, Items, HostA, body);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], ErrorKeys(problem));
        using var list = await GetJsonAsync(Items, HostA);
        Assert.Equal(update ? 1 : 0, list.RootElement.GetProperty("totalCount").GetInt64());
        if (existing is not null)
        {
            using var record = await GetJsonAsync($"{Items}/{existing}", HostA);
            Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        }
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData(null)]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; boundary=x")]
    public async Task Body_that_is_not_application_json_is_a_415_problem_and_writes_nothing(string? contentType)
    {
        await fixture.ResetAsync();
        var id = await CreateItemAsync(HostA, """{ "name": "Desk" }""");

        using var post = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair" } }""", contentType);
        using var postResponse = await fixture.Client.SendAsync(post, CancellationToken);
        using var patch = Request(HttpMethod.Patch, $"{Items}/{id}", HostA, """{ "version": 1, "values": { "name": "Chair" } }""", contentType);
        using var patchResponse = await fixture.Client.SendAsync(patch, CancellationToken);

        foreach (var response in new[] { postResponse, patchResponse })
        {
            using var problem = await ReadProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
        }

        using var list = await GetJsonAsync(Items, HostA);
        Assert.Equal(1, list.RootElement.GetProperty("totalCount").GetInt64());
        using var record = await GetJsonAsync($"{Items}/{id}", HostA);
        Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("\"Desk\"", record.RootElement.GetProperty("values").GetProperty("name").GetRawText());
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("Application/JSON; Charset=\"UTF-8\"")]
    public async Task Json_with_a_utf8_charset_in_any_letter_case_is_accepted(string contentType)
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""", contentType);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_unique_value_is_a_409_problem_keyed_by_the_field()
    {
        await fixture.ResetAsync();
        await CreateDepartmentAsync(HostA, "Sales");
        var other = await CreateDepartmentAsync(HostA, "Finance");

        using var create = Request(HttpMethod.Post, Departments, HostA, """{ "values": { "name": "Sales" } }""");
        using var createResponse = await fixture.Client.SendAsync(create, CancellationToken);
        using var update = Request(HttpMethod.Patch, $"{Departments}/{other:D}", HostA, """{ "version": 1, "values": { "name": "Sales" } }""");
        using var updateResponse = await fixture.Client.SendAsync(update, CancellationToken);

        foreach (var response in new[] { createResponse, updateResponse })
        {
            using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal(["/values/name"], ErrorKeys(problem));
        }

        using var list = await GetJsonAsync($"{Departments}?sort=name", HostA);
        Assert.Equal(
            ["Finance", "Sales"],
            list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("values").GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Not_null_column_the_model_does_not_require_is_a_409_problem_that_names_no_column()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(item.Id));

        // The fixture is shared by the class, so the column is dropped whatever the outcome.
        await fixture.ExecuteAsync(TenantA, $"""ALTER TABLE {table} ADD COLUMN "f_extra" text NOT NULL""");
        try
        {
            using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""");
            using var response = await fixture.Client.SendAsync(request, CancellationToken);

            using var problem = await ReadProblemAsync(response, HttpStatusCode.Conflict);
            Assert.DoesNotContain("extra", problem.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal("The stored schema does not match the active model.", problem.RootElement.GetProperty("title").GetString());
        }
        finally
        {
            await fixture.ExecuteAsync(TenantA, $"""ALTER TABLE {table} DROP COLUMN "f_extra" """);
        }
    }

    [Fact]
    public async Task Update_through_another_tenant_host_is_a_404_and_changes_nothing()
    {
        await fixture.ResetAsync();
        var id = await CreateDepartmentAsync(HostA, "Sales");

        using var request = Request(HttpMethod.Patch, $"{Departments}/{id:D}", HostB, """{ "version": 1, "values": { "name": "Finance" } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        using var record = await GetJsonAsync($"{Departments}/{id:D}", HostA);
        Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("\"Sales\"", record.RootElement.GetProperty("values").GetProperty("name").GetRawText());
    }

    [Fact]
    public async Task Update_of_an_unknown_record_is_a_404_problem()
    {
        await fixture.ResetAsync();

        using var request = Request(HttpMethod.Patch, $"{Items}/{Guid.NewGuid():D}", HostA, """{ "version": 1, "values": {} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateDepartmentAsync(string host, string name)
    {
        using var request = Request(HttpMethod.Post, Departments, host, $$"""{ "values": { "name": "{{name}}" } }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = await ReadJsonAsync(response);
        return record.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<string> CreateItemAsync(string host, string values)
    {
        using var request = Request(HttpMethod.Post, Items, host, $$"""{ "values": {{values}} }""");
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var record = await ReadJsonAsync(response);
        return record.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<JsonDocument> PatchJsonAsync(string id, string body, HttpStatusCode expected)
    {
        using var request = Request(HttpMethod.Patch, $"{Items}/{id}", HostA, body);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        if (expected != HttpStatusCode.OK)
        {
            return await ReadProblemAsync(response, expected);
        }

        Assert.Equal(expected, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string host)
    {
        using var request = Request(path, host);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static List<string> ErrorKeys(JsonDocument problem) =>
        problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).ToList();
}
