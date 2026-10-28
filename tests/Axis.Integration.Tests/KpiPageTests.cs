using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class KpiPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string ShowDepartments = """{"type":"navigate","page":"Departments","labelKey":"itemKpi.showDepartments","parameters":{},"record":null,"opensForm":false,"paths":{},"visibleWhen":null}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_who_can_run_the_data_source_gets_the_kpi_with_its_measures_and_navigate_and_no_access()
    {
        using var page = await GetPageAsync("requester", "itemkpi");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("kpi", widget.GetProperty("type").GetString());
        Assert.Equal("ItemTotals", widget.GetProperty("dataSource").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("entity").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("form").ValueKind);
        Assert.Equal(0, widget.GetProperty("actions").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("access").ValueKind);
        Assert.Equal(
            $$"""{"measure":"items","labelKey":"itemKpi.items","comparison":{"measure":"priceSum","labelKey":"itemKpi.priceSum"},"navigate":{{ShowDepartments}}}""",
            widget.GetProperty("kpi").GetRawText());
        Assert.Equal("chart", widget.EnumerateObject().Last().Name);
    }

    [Fact]
    public async Task Widget_that_is_not_a_kpi_has_a_null_kpi()
    {
        using var page = await GetPageAsync("requester", "items");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("kpi").ValueKind);
    }

    [Fact]
    public async Task User_who_cannot_read_the_target_page_gets_the_kpi_with_a_null_navigate()
    {
        // Fern runs ItemTotals, but holds no grant on departments.
        using var page = await GetPageAsync("fern", "itemkpi");

        var kpi = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList()).GetProperty("kpi");
        Assert.Equal(
            """{"measure":"items","labelKey":"itemKpi.items","comparison":{"measure":"priceSum","labelKey":"itemKpi.priceSum"},"navigate":null}""",
            kpi.GetRawText());
    }

    [Theory]
    [InlineData("rita")]
    [InlineData("fia")]
    public async Task Page_whose_only_widget_is_a_kpi_the_user_cannot_run_is_a_403_problem(string user)
    {
        // Rita holds no grant on the data source. Fia holds one, but cannot read the quantity and the price it projects.
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request("/api/sites/records/pages/itemkpi", HostA);

        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Kpi_the_user_cannot_run_is_left_out_of_a_page_with_other_readable_widgets()
    {
        using var page = await GetPageAsync("rita", "itemkpioverview");

        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        var column = Assert.Single(columns.GetProperty("columns").EnumerateArray().ToList());
        Assert.Equal(8, column.GetProperty("width").GetInt32());
        var table = Assert.Single(column.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("table", table.GetProperty("type").GetString());
    }

    [Fact]
    public async Task User_who_can_run_the_data_source_gets_the_kpi_next_to_the_table()
    {
        using var page = await GetPageAsync("requester", "itemkpioverview");

        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        var kpi = Assert.Single(columns.GetProperty("columns").EnumerateArray().ToList()[1].GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(
            """{"measure":"items","labelKey":"itemKpi.items","comparison":null,"navigate":null}""",
            kpi.GetProperty("kpi").GetRawText());
    }

    [Fact]
    public async Task Site_navigation_does_not_list_the_kpi_pages()
    {
        using var site = await GetSiteAsync("requester");

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
