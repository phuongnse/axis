using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Departments = "/api/apps/RecordsApp/entities/Department/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_returns_every_field_type_in_its_json_shape_and_null_for_missing_values()
    {
        await fixture.ResetAsync();
        var departmentId = await InsertDepartmentAsync(TenantA, "Sales");
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = "Desk",
            ["quantity"] = "3",
            ["price"] = "123456789012345678.90",
            ["active"] = "true",
            ["neededBy"] = "2026-10-06",
            ["orderedAt"] = "2026-10-06T09:00:00.123456+07:00",
            ["status"] = "open",
            ["department"] = departmentId.ToString(),
        });
        var bareId = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Chair" });

        using var record = await GetJsonAsync($"{Items}/{id}", HostA);

        var root = record.RootElement;
        Assert.Equal(["id", "version", "values", "labels"], root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(id.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt64());
        Assert.Equal(
            [
                ("name", "\"Desk\""),
                ("quantity", "3"),
                ("price", "123456789012345678.90"),
                ("active", "true"),
                ("neededBy", "\"2026-10-06\""),
                ("orderedAt", "\"2026-10-06T02:00:00.123456Z\""),
                ("status", "\"open\""),
                ("parts", "[]"),
                ("department", $"\"{departmentId:D}\""),
                ("total", "null"),
            ],
            root.GetProperty("values").EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())));
        Assert.Equal("""{"department":"Sales"}""", root.GetProperty("labels").GetRawText());

        using var bare = await GetJsonAsync($"{Items}/{bareId}", HostA);

        var bareValues = bare.RootElement.GetProperty("values").EnumerateObject().ToList();
        Assert.Equal("\"Chair\"", bareValues[0].Value.GetRawText());
        Assert.Equal(10, bareValues.Count);
        Assert.Equal("[]", bareValues[7].Value.GetRawText());
        Assert.All(bareValues.Skip(1).Where((_, index) => index != 6), property => Assert.Equal(JsonValueKind.Null, property.Value.ValueKind));
        Assert.Equal("{}", bare.RootElement.GetProperty("labels").GetRawText());
    }

    [Fact]
    public async Task List_and_get_carry_the_display_field_of_each_reference_as_a_label()
    {
        await fixture.ResetAsync();
        var financeId = await InsertDepartmentAsync(TenantA, "Finance");
        var salesId = await InsertDepartmentAsync(TenantA, "Sales");
        var deskId = await InsertItemAsync("Desk", financeId);
        var labels = new Dictionary<Guid, string>
        {
            [deskId] = """{"department":"Finance"}""",
            [await InsertItemAsync("Chair", salesId)] = """{"department":"Sales"}""",
            [await InsertItemAsync("Lamp", null)] = "{}",
        };

        using var list = await GetJsonAsync(Items, HostA);
        using var desk = await GetJsonAsync($"{Items}/{deskId}", HostA);
        using var department = await GetJsonAsync($"{Departments}/{financeId}", HostA);

        var items = list.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(labels[item.GetProperty("id").GetGuid()], item.GetProperty("labels").GetRawText()));
        Assert.Equal("""{"department":"Finance"}""", desk.RootElement.GetProperty("labels").GetRawText());
        Assert.Equal(financeId.ToString("D"), desk.RootElement.GetProperty("values").GetProperty("department").GetString());
        Assert.Equal("{}", department.RootElement.GetProperty("labels").GetRawText());
    }

    [Fact]
    public async Task List_returns_the_requested_page_ordered_by_sort_then_by_id()
    {
        await fixture.ResetAsync();
        var ids = new Dictionary<string, Guid>();
        foreach (var name in new[] { "B", "C", "A" })
        {
            ids[name] = await InsertDepartmentAsync(TenantA, name);
        }

        using var first = await GetJsonAsync($"{Departments}?sort=name&page=1&pageSize=2", HostA);
        using var second = await GetJsonAsync($"{Departments}?sort=name&page=2&pageSize=2", HostA);
        using var descending = await GetJsonAsync($"{Departments}?sort=-name&page=1&pageSize=2", HostA);
        using var pastTheEnd = await GetJsonAsync($"{Departments}?page=3&pageSize=2", HostA);

        Assert.Equal((1, 2, 3L), Paging(first));
        Assert.Equal([ids["A"], ids["B"]], ItemIds(first));
        Assert.Equal((2, 2, 3L), Paging(second));
        Assert.Equal([ids["C"]], ItemIds(second));
        Assert.Equal([ids["C"], ids["B"]], ItemIds(descending));
        Assert.Equal((3, 2, 3L), Paging(pastTheEnd));
        Assert.Empty(ItemIds(pastTheEnd));

        var tied = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            tied.Add(await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = $"Item {index}", ["quantity"] = "7" }));
        }

        // PostgreSQL orders uuid values as their lowercase hex text.
        var byId = tied.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToList();
        using var ascendingTies = await GetJsonAsync($"{Items}?sort=quantity", HostA);
        using var descendingTies = await GetJsonAsync($"{Items}?sort=-quantity", HostA);
        using var withoutSort = await GetJsonAsync(Items, HostA);

        Assert.Equal(byId, ItemIds(ascendingTies));
        Assert.Equal(byId, ItemIds(descendingTies));
        Assert.Equal(byId, ItemIds(withoutSort));
        Assert.Equal((1, 20, 3L), Paging(withoutSort));
    }

    public static TheoryData<string> MissingPaths => new()
    {
        "/api/apps/Unknown/entities/Item/records",
        "/api/apps/RecordsApp/entities/Unknown/records",
        $"/api/apps/RecordsApp/entities/Item/records/{Guid.NewGuid()}",
        "/api/apps/RecordsApp/entities/Item/records/not-a-guid",
        $"/api/apps/RecordsApp/entities/Item/records/{Guid.NewGuid():N}",
    };

    [Theory]
    [MemberData(nameof(MissingPaths))]
    public async Task Path_that_names_no_active_application_entity_or_record_is_a_404_problem(string path)
    {
        await fixture.ResetAsync();

        await AssertNotFoundAsync(path, HostA);
    }

    [Fact]
    public async Task Application_with_a_release_but_no_active_release_is_a_404_problem()
    {
        await fixture.ResetAsync();

        await AssertNotFoundAsync($"/api/apps/{fixture.DormantApp}/entities/Item/records", HostA);
    }

    [Fact]
    public async Task Invalid_page_page_size_and_sort_are_reported_together_in_one_400_problem()
    {
        await fixture.ResetAsync();
        using var request = Request($"{Items}?page=0&pageSize=101&sort=missing", HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["page", "pageSize", "sort"], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("page=1&page=2", "page")]
    [InlineData("pageSize=%2B5", "pageSize")]
    [InlineData("sort=Name", "sort")]
    [InlineData("sort=--name", "sort")]
    public async Task Repeated_signed_or_wrong_case_parameter_is_a_400_problem(string query, string key)
    {
        await fixture.ResetAsync();
        using var request = Request($"{Items}?{query}", HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Child_collection_is_not_a_valid_sort()
    {
        await fixture.ResetAsync();
        using var request = Request($"{Items}?sort=-parts", HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["sort"], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Row_of_tenant_a_is_listed_and_read_only_through_the_host_of_tenant_a()
    {
        await fixture.ResetAsync();
        var id = await InsertDepartmentAsync(TenantA, "Finance");

        // The application and entity names match ignoring letter case.
        using var listedInA = await GetJsonAsync("/api/apps/recordsapp/entities/DEPARTMENT/records", HostA);
        using var readInA = await GetJsonAsync($"{Departments}/{id}", HostA);
        using var listedInB = await GetJsonAsync(Departments, HostB);

        Assert.Equal([id], ItemIds(listedInA));
        Assert.Equal("\"Finance\"", readInA.RootElement.GetProperty("values").GetProperty("name").GetRawText());
        Assert.Empty(ItemIds(listedInB));
        Assert.Equal(0, listedInB.RootElement.GetProperty("totalCount").GetInt64());
        await AssertNotFoundAsync($"{Departments}/{id}", HostB);
    }

    private Task<Guid> InsertDepartmentAsync(string tenant, string name) =>
        fixture.InsertAsync(tenant, "Department", new Dictionary<string, string?> { ["name"] = name });

    private Task<Guid> InsertItemAsync(string name, Guid? departmentId) =>
        fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = name, ["department"] = departmentId?.ToString() });

    private async Task<JsonDocument> GetJsonAsync(string path, string host)
    {
        using var request = Request(path, host);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task AssertNotFoundAsync(string path, string host)
    {
        using var request = Request(path, host);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
    }

    private static (int Page, int PageSize, long TotalCount) Paging(JsonDocument list) =>
        (list.RootElement.GetProperty("page").GetInt32(),
            list.RootElement.GetProperty("pageSize").GetInt32(),
            list.RootElement.GetProperty("totalCount").GetInt64());

    private static List<Guid> ItemIds(JsonDocument list) =>
        list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
