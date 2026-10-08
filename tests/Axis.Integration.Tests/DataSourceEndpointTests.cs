using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class DataSourceEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    // ItemsByName projects name, quantity, price as unitPrice and department, sorted by name, two rows a page.
    private const string Rows = "/api/apps/RecordsApp/data-sources/ItemsByName/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rows_hold_only_the_projected_fields_under_their_names_with_the_default_sort_and_page_size()
    {
        await fixture.ResetAsync();
        var departmentId = await InsertDepartmentAsync(TenantA, "Sales");
        var ids = new Dictionary<string, Guid>
        {
            ["C"] = await InsertItemAsync(TenantA, "C", "3", null),
            ["A"] = await InsertItemAsync(TenantA, "A", "1", departmentId),
            ["B"] = await InsertItemAsync(TenantA, "B", "2", null),
        };

        using var rows = await GetJsonAsync(Rows, HostA);
        using var emptyQuery = await GetJsonAsync($"{Rows}?page=&pageSize=&sort=", HostA);

        Assert.Equal((1, 2, 3L), Paging(rows));
        Assert.Equal([ids["A"], ids["B"]], RowIds(rows));
        Assert.Equal((1, 2, 3L), Paging(emptyQuery));
        Assert.Equal([ids["A"], ids["B"]], RowIds(emptyQuery));
        var first = rows.RootElement.GetProperty("items")[0];
        Assert.Equal(["id", "values", "labels"], first.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                ("name", "\"A\""),
                ("quantity", "1"),
                ("unitPrice", "12.50"),
                ("department", $"\"{departmentId:D}\""),
            ],
            first.GetProperty("values").EnumerateObject().Select(property => (property.Name, property.Value.GetRawText())));
        Assert.Equal("""{"department":"Sales"}""", first.GetProperty("labels").GetRawText());
        var second = rows.RootElement.GetProperty("items")[1];
        Assert.Equal(JsonValueKind.Null, second.GetProperty("values").GetProperty("department").ValueKind);
        Assert.Equal("{}", second.GetProperty("labels").GetRawText());
    }

    [Fact]
    public async Task Sort_and_paging_follow_the_record_api_rules_with_the_root_id_as_tie_break()
    {
        await fixture.ResetAsync();
        var ids = new Dictionary<string, Guid>();
        foreach (var (name, quantity) in new[] { ("A", "1"), ("B", "3"), ("C", "2") })
        {
            ids[name] = await InsertItemAsync(TenantA, name, quantity, null);
        }

        using var second = await GetJsonAsync($"{Rows}?sort=-quantity&page=2&pageSize=1", HostA);
        using var pastTheEnd = await GetJsonAsync($"{Rows}?page=3", HostA);

        Assert.Equal((2, 1, 3L), Paging(second));
        Assert.Equal([ids["C"]], RowIds(second));
        Assert.Equal((3, 2, 3L), Paging(pastTheEnd));
        Assert.Empty(RowIds(pastTheEnd));

        await fixture.ResetAsync();
        var tied = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            tied.Add(await InsertItemAsync(TenantA, $"Item {index}", "7", null));
        }

        // PostgreSQL orders uuid values as their lowercase hex text.
        var byId = tied.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToList();
        using var ascendingTies = await GetJsonAsync($"{Rows}?sort=quantity&pageSize=3", HostA);
        using var descendingTies = await GetJsonAsync($"{Rows}?sort=-quantity&pageSize=3", HostA);

        Assert.Equal(byId, RowIds(ascendingTies));
        Assert.Equal(byId, RowIds(descendingTies));
    }

    [Theory]
    [InlineData("sort=active", "sort")]
    [InlineData("sort=price", "sort")]
    [InlineData("sort=department", "sort")]
    [InlineData("sort=Name", "sort")]
    [InlineData("sort=--name", "sort")]
    [InlineData("page=0", "page")]
    [InlineData("page=1&page=2", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=%2B5", "pageSize")]
    public async Task Invalid_query_parameter_is_a_400_problem_keyed_by_the_parameter(string query, string key)
    {
        await fixture.ResetAsync();
        using var request = Request($"{Rows}?{query}", HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var body = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal([key], body.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task Rows_of_each_tenant_are_returned_only_through_the_host_of_that_tenant()
    {
        await fixture.ResetAsync();
        var inA = await InsertItemAsync(TenantA, "Desk", "1", null);
        var inB = await InsertItemAsync(TenantB, "Chair", "2", null);

        // The application and data source names match ignoring letter case.
        using var rowsInA = await GetJsonAsync("/api/apps/recordsapp/data-sources/ITEMSBYNAME/rows", HostA);
        using var rowsInB = await GetJsonAsync(Rows, HostB);

        Assert.Equal([inA], RowIds(rowsInA));
        Assert.Equal(1, rowsInA.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal([inB], RowIds(rowsInB));
        Assert.Equal(1, rowsInB.RootElement.GetProperty("totalCount").GetInt64());
    }

    public static TheoryData<string> MissingPaths => new()
    {
        "/api/apps/Unknown/data-sources/ItemsByName/rows",
        "/api/apps/RecordsApp/data-sources/Unknown/rows",
        "/api/apps/SecondApp/data-sources/ItemsByName/rows",
    };

    [Theory]
    [MemberData(nameof(MissingPaths))]
    public async Task Path_that_names_no_active_application_or_data_source_is_a_404_problem_whatever_its_query(string path)
    {
        await fixture.ResetAsync();

        await AssertNotFoundAsync(path, HostA);
        await AssertNotFoundAsync($"{path}?page=0&sort=missing", HostA);
    }

    [Fact]
    public async Task Application_with_a_release_but_no_active_release_is_a_404_problem()
    {
        await fixture.ResetAsync();

        await AssertNotFoundAsync($"/api/apps/{fixture.DormantApp}/data-sources/ItemsByName/rows", HostA);
    }

    [Fact]
    public async Task Only_get_is_routed()
    {
        await fixture.ResetAsync();
        using var request = Request(HttpMethod.Post, Rows, HostA, "{}");

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private Task<Guid> InsertDepartmentAsync(string tenant, string name) =>
        fixture.InsertAsync(tenant, "Department", new Dictionary<string, string?> { ["name"] = name });

    private Task<Guid> InsertItemAsync(string tenant, string name, string quantity, Guid? departmentId) =>
        fixture.InsertAsync(tenant, "Item", new Dictionary<string, string?>
        {
            ["name"] = name,
            ["quantity"] = quantity,
            ["price"] = "12.50",
            ["active"] = "true",
            ["department"] = departmentId?.ToString(),
        });

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

    private static (int Page, int PageSize, long TotalCount) Paging(JsonDocument rows) =>
        (rows.RootElement.GetProperty("page").GetInt32(),
            rows.RootElement.GetProperty("pageSize").GetInt32(),
            rows.RootElement.GetProperty("totalCount").GetInt64());

    private static List<Guid> RowIds(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
}
