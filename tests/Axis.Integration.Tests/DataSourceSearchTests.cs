using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A data source with <c>"search": true</c> takes <c>search</c> on its rows route. It matches the projected
/// <c>searchable</c> fields and the directly projected display field, ignoring letter case, before any grouping.
/// Without the option, a given <c>search</c> is a 400. In RecordsApp, Department.name is searchable and
/// the display field, and Item.name is not searchable, so only an item's department name matches.
/// </summary>
public sealed class DataSourceSearchTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/data-sources/SearchableItems/rows";

    private const string Departments = "/api/apps/RecordsApp/data-sources/DepartmentNames/rows";

    private const string Counts = "/api/apps/RecordsApp/data-sources/SearchableItemCounts/rows";

    private const string WithoutOption = "/api/apps/RecordsApp/data-sources/ItemsByName/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Search_keeps_only_the_rows_where_a_searchable_projected_field_contains_the_text_ignoring_letter_case()
    {
        await fixture.ResetAsync();
        var (desk, _, _) = await InsertItemsAsync();
        await InsertDepartmentAsync("Marketing");

        using var items = await GetJsonAsync($"{Items}?search=SAL");
        using var departments = await GetJsonAsync($"{Departments}?search=fin");
        using var everything = await GetJsonAsync($"{Items}?search=");

        // Item.name is not searchable, so 'Sales binder' has no department to match.
        Assert.Equal([desk], RowIds(items));
        Assert.Equal(1L, items.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(["Finance"], DepartmentNamesOf(departments));
        Assert.Equal(1L, departments.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(3L, everything.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task Search_on_a_grouped_data_source_filters_the_root_rows_before_grouping()
    {
        await fixture.ResetAsync();
        var sales = await InsertDepartmentAsync("Sales");
        var finance = await InsertDepartmentAsync("Finance");
        await InsertItemAsync("Desk", sales);
        await InsertItemAsync("Lamp", sales);
        await InsertItemAsync("Chair", finance);

        using var groups = await GetJsonAsync($"{Counts}?search=sal");
        using var all = await GetJsonAsync(Counts);

        var group = Assert.Single(groups.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Sales", group.GetProperty("values").GetProperty("departmentName").GetString());
        Assert.Equal(2L, group.GetProperty("values").GetProperty("count").GetInt64());
        Assert.Equal(1L, groups.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(2L, all.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData(WithoutOption, "search=x")]
    [InlineData(WithoutOption, "search=a&search=b")]
    [InlineData(WithoutOption, "search=&search=")]
    [InlineData(Items, "search=a&search=b")]
    [InlineData(Items, "search=%00")]
    public async Task Search_that_is_not_allowed_is_a_400_problem_keyed_search(string route, string query)
    {
        await fixture.ResetAsync();

        await AssertSearchRefusedAsync($"{route}?{query}");
    }

    [Fact]
    public async Task Search_of_more_than_200_characters_is_a_400_problem_keyed_search_and_200_characters_are_allowed()
    {
        await fixture.ResetAsync();

        await AssertSearchRefusedAsync($"{Items}?search={new string('a', 201)}");

        using var allowed = await GetJsonAsync($"{Items}?search={new string('a', 200)}");
        Assert.Empty(RowIds(allowed));
    }

    [Fact]
    public async Task An_empty_search_is_ignored_on_a_data_source_without_the_option()
    {
        await fixture.ResetAsync();
        await InsertItemsAsync();

        using var rows = await GetJsonAsync($"{WithoutOption}?search=&pageSize=3");

        Assert.Equal(3, RowIds(rows).Count);
        Assert.Equal(3L, rows.RootElement.GetProperty("totalCount").GetInt64());
    }

    private async Task AssertSearchRefusedAsync(string path)
    {
        using var request = Request(path, HostA);

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["search"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    // Desk is in Sales, Chair in Finance and 'Sales binder' has no department.
    private async Task<(Guid Desk, Guid Chair, Guid Binder)> InsertItemsAsync()
    {
        var sales = await InsertDepartmentAsync("Sales");
        var finance = await InsertDepartmentAsync("Finance");
        return (
            await InsertItemAsync("Desk", sales),
            await InsertItemAsync("Chair", finance),
            await InsertItemAsync("Sales binder", null));
    }

    private Task<Guid> InsertDepartmentAsync(string name) =>
        fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = name });

    private Task<Guid> InsertItemAsync(string name, Guid? departmentId) =>
        fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = name,
            ["quantity"] = "1",
            ["department"] = departmentId?.ToString(),
        });

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> RowIds(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();

    private static List<string?> DepartmentNamesOf(JsonDocument rows) =>
        rows.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("name").GetString())
            .ToList();
}
