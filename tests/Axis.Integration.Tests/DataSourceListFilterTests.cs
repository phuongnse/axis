using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The rows route of a data source and its export take <c>filter</c>, the record API's filter over the projected columns,
/// or over the group fields of a grouped data source. It narrows the rows on top of the data source's own filter and the
/// user's read filters, and on a grouped data source it applies to the root records before grouping. In RecordsApp,
/// <c>ItemsByDepartment</c> drops items of the department Archive, and <c>eve</c> reads only items with a status.
/// </summary>
public sealed class DataSourceListFilterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/data-sources/ItemsByDepartment";

    private const string Counts = "/api/apps/RecordsApp/data-sources/ItemCountsByDepartment/rows";

    private const string SalesOrArchive =
        """{"any":[{"field":"departmentName","op":"eq","value":"Sales"},{"field":"departmentName","op":"eq","value":"Archive"}]}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filter_keeps_only_matching_rows_on_top_of_the_data_source_filter_and_the_read_filter_in_the_list_and_the_export()
    {
        await fixture.ResetAsync();
        var sales = await InsertDepartmentAsync("Sales");
        var archive = await InsertDepartmentAsync("Archive");
        var ops = await InsertDepartmentAsync("Ops");
        var alpha = await InsertItemAsync("Alpha", sales, "open");
        await InsertItemAsync("Beta", sales, null);
        await InsertItemAsync("Gamma", archive, "open");
        var delta = await InsertItemAsync("Delta", ops, "open");
        using var eve = fixture.CreateClient().SignedInAs("eve");

        // Without the filter, eve's read filter hides Beta and the data source filter hides Gamma.
        using var everything = await GetJsonAsync(eve, $"{Items}/rows");
        using var rows = await GetJsonAsync(eve, $"{Items}/rows?filter={Uri.EscapeDataString(SalesOrArchive)}");
        using var export = await SendAsync(eve, $"{Items}/export?format=csv&filter={Uri.EscapeDataString(SalesOrArchive)}");

        Assert.Equal([.. new[] { alpha, delta }.Order()], RowIds(everything));
        Assert.Equal([alpha], RowIds(rows));
        Assert.Equal(1L, rows.RootElement.GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csv = CsvText.Parse(Encoding.UTF8.GetString((await export.Content.ReadAsByteArrayAsync(CancellationToken))[3..]));
        var cells = csv.SelectMany(row => row).ToList();
        Assert.Equal(2, csv.Count);
        Assert.Contains("Alpha", cells);
        Assert.DoesNotContain(cells, cell => cell is "Beta" or "Gamma" or "Delta");
    }

    [Fact]
    public async Task Filter_on_a_grouped_data_source_covers_only_the_root_records_that_match_a_group_field()
    {
        await fixture.ResetAsync();
        var sales = await InsertDepartmentAsync("Sales");
        var ops = await InsertDepartmentAsync("Ops");
        await InsertItemAsync("Alpha", sales, "open");
        await InsertItemAsync("Beta", sales, null);
        await InsertItemAsync("Delta", ops, "open");
        var filter = Uri.EscapeDataString("""{"all":[{"field":"departmentName","op":"eq","value":"Sales"}]}""");

        using var all = await GetJsonAsync(fixture.Client, Counts);
        using var groups = await GetJsonAsync(fixture.Client, $"{Counts}?filter={filter}");

        Assert.Equal(2L, all.RootElement.GetProperty("totalCount").GetInt64());
        var group = Assert.Single(groups.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Sales", group.GetProperty("values").GetProperty("departmentName").GetString());
        Assert.Equal(2L, group.GetProperty("values").GetProperty("items").GetInt64());
        Assert.Equal(2L, group.GetProperty("values").GetProperty("quantitySum").GetInt64());
        Assert.Equal(1L, groups.RootElement.GetProperty("totalCount").GetInt64());
    }

    [Theory]
    [InlineData("ItemsByDepartment", """{"all":[{"field":"quantity","op":"eq","value":1}]}""")] // not projected
    [InlineData("ItemCountsByDepartment", """{"all":[{"field":"items","op":"eq","value":1}]}""")] // a measure
    [InlineData("ItemCountsByDepartment", """{"all":[{"field":"quantity","op":"eq","value":1}]}""")] // projected, but not a group field
    [InlineData("ItemsByDepartment", """{"all":[{"field":"name","op":"eq","value":5}]}""")] // a value of the wrong kind
    [InlineData("ItemsByDepartment", "{not json")]
    public async Task A_filter_that_cannot_be_used_is_a_400_problem_keyed_filter_in_the_list_and_the_export(string dataSource, string filter)
    {
        await fixture.ResetAsync();
        var path = $"/api/apps/RecordsApp/data-sources/{dataSource}";
        var query = $"filter={Uri.EscapeDataString(filter)}";

        await AssertFilterRefusedAsync($"{path}/rows?{query}");
        await AssertFilterRefusedAsync($"{path}/export?format=csv&{query}");
    }

    [Fact]
    public async Task A_repeated_filter_is_a_400_problem_keyed_filter_in_the_list_and_the_export()
    {
        await fixture.ResetAsync();
        var filter = Uri.EscapeDataString("""{"all":[{"field":"name","op":"eq","value":"Alpha"}]}""");
        var query = $"filter={filter}&filter={filter}";

        await AssertFilterRefusedAsync($"{Items}/rows?{query}");
        await AssertFilterRefusedAsync($"{Items}/export?format=csv&{query}");
    }

    [Fact]
    public async Task An_empty_filter_is_ignored()
    {
        await fixture.ResetAsync();
        var sales = await InsertDepartmentAsync("Sales");
        var alpha = await InsertItemAsync("Alpha", sales, "open");

        using var rows = await GetJsonAsync(fixture.Client, $"{Items}/rows?filter=");

        Assert.Equal([alpha], RowIds(rows));
    }

    private async Task AssertFilterRefusedAsync(string path)
    {
        using var eve = fixture.CreateClient().SignedInAs("eve");
        using var response = await SendAsync(eve, path);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(["filter"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    private Task<Guid> InsertDepartmentAsync(string name) =>
        fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = name });

    private Task<Guid> InsertItemAsync(string name, Guid department, string? status) =>
        fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
        {
            ["name"] = name,
            ["quantity"] = "1",
            ["status"] = status,
            ["department"] = department.ToString("D"),
        });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using var request = Request(path, HostA);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> RowIds(JsonDocument rows) =>
        [.. rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order()];
}
