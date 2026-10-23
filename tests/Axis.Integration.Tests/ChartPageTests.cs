using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ChartPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_who_can_run_the_data_source_gets_the_chart_with_its_series_and_no_access()
    {
        using var page = await GetPageAsync("anna", "itemchart");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("chart", widget.GetProperty("type").GetString());
        Assert.Equal("ItemCountsByDepartment", widget.GetProperty("dataSource").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("entity").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("form").ValueKind);
        Assert.Equal(0, widget.GetProperty("actions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("access").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("kpi").ValueKind);
        Assert.Equal(
            """{"kind":"bar","titleKey":"itemChart.title","category":"departmentName","measures":[{"name":"items","labelKey":"itemChart.items"},{"name":"quantitySum","labelKey":"itemChart.quantitySum"}]}""",
            widget.GetProperty("chart").GetRawText());
        Assert.Equal("chart", widget.EnumerateObject().Last().Name);
    }

    [Fact]
    public async Task Widget_that_is_not_a_chart_has_a_null_chart()
    {
        using var page = await GetPageAsync("anna", "items");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("chart").ValueKind);
    }

    [Fact]
    public async Task Page_whose_only_widget_is_a_chart_the_user_cannot_run_is_a_403_problem()
    {
        // Rita holds no grant on the data source.
        using var client = fixture.CreateClient().SignedInAs("rita");
        using var request = Request("/api/sites/records/pages/itemchart", HostA);

        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Chart_the_user_cannot_run_is_left_out_of_a_page_with_other_readable_widgets()
    {
        using var page = await GetPageAsync("rita", "itemchartoverview");

        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        var column = Assert.Single(columns.GetProperty("columns").EnumerateArray().ToList());
        Assert.Equal(8, column.GetProperty("width").GetInt32());
        var table = Assert.Single(column.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("table", table.GetProperty("type").GetString());
    }

    [Fact]
    public async Task User_who_can_run_the_data_source_gets_the_chart_next_to_the_table()
    {
        using var page = await GetPageAsync("anna", "itemchartoverview");

        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        var chart = Assert.Single(columns.GetProperty("columns").EnumerateArray().ToList()[1].GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(
            """{"kind":"pie","titleKey":"itemChart.title","category":"departmentName","measures":[{"name":"items","labelKey":"itemChart.items"}]}""",
            chart.GetProperty("chart").GetRawText());
    }

    [Fact]
    public async Task Site_navigation_does_not_list_the_chart_pages()
    {
        using var site = await GetSiteAsync("anna");

        Assert.Equal(
            ["Items", "Departments"],
            site.RootElement.GetProperty("navigation").EnumerateArray().Select(entry => entry.GetProperty("page").GetString()));
    }

    private async Task<JsonDocument> GetPageAsync(string user, string page)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request($"/api/sites/records/pages/{page}", HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<JsonDocument> GetSiteAsync(string user)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request("/api/sites/records", HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
