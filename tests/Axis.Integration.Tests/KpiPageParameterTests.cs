using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A kpi binds a page parameter into its data source with <c>parameters</c>. The page metadata carries the
/// binding, the total follows the parameter's value, and the data source's read grant still applies.
/// ItemTotalsInDepartment keeps the items of the department that its required parameter inDepartment names, with
/// items = count and quantitySum = sum(quantity). ItemDepartmentKpi binds its department page parameter to it.
/// </summary>
public sealed class KpiPageParameterTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Rows = "/api/apps/RecordsApp/data-sources/ItemTotalsInDepartment/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Page_metadata_carries_the_binding_of_the_kpi_and_the_page_parameter()
    {
        using var page = await GetAsync("requester", "/api/sites/records/pages/itemdepartmentkpi", HttpStatusCode.OK);

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("kpi", widget.GetProperty("type").GetString());
        Assert.Equal("ItemTotalsInDepartment", widget.GetProperty("dataSource").GetProperty("name").GetString());
        Assert.Equal("""{"inDepartment":"department"}""", widget.GetProperty("parameters").GetRawText());
        var pageParameter = Assert.Single(page.RootElement.GetProperty("parameters").EnumerateArray().ToList());
        Assert.Equal("department", pageParameter.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Total_follows_the_department_that_the_bound_parameter_names_and_a_missing_value_is_a_400_problem()
    {
        await fixture.ResetAsync();
        var sales = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var ops = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Ops" });
        foreach (var (name, department, quantity) in new[] { ("S1", sales, "2"), ("S2", sales, "5"), ("S3", sales, "1"), ("O1", ops, "4") })
        {
            await fixture.InsertAsync(
                TenantA,
                "Item",
                new Dictionary<string, string?> { ["name"] = name, ["department"] = department.ToString("D"), ["quantity"] = quantity });
        }

        using var inSales = await GetAsync("requester", $"{Rows}?inDepartment={sales:D}", HttpStatusCode.OK);
        using var inOps = await GetAsync("requester", $"{Rows}?inDepartment={ops:D}", HttpStatusCode.OK);
        using var missing = await GetAsync("requester", Rows, HttpStatusCode.BadRequest);

        Assert.Equal("""{"items":3,"quantitySum":8}""", TotalRow(inSales));
        Assert.Equal("""{"items":1,"quantitySum":4}""", TotalRow(inOps));
        Assert.Equal(["inDepartment"], missing.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task User_without_the_data_source_read_grant_gets_no_page_and_no_rows()
    {
        await fixture.ResetAsync();
        var sales = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });

        // Rita holds no grant on the data source.
        using var page = await GetAsync("rita", "/api/sites/records/pages/itemdepartmentkpi", HttpStatusCode.Forbidden);
        using var rows = await GetAsync("rita", $"{Rows}?inDepartment={sales:D}", HttpStatusCode.Forbidden);

        Assert.Equal("You have no grant for this action.", page.RootElement.GetProperty("title").GetString());
        Assert.Equal("You have no grant for this action.", rows.RootElement.GetProperty("title").GetString());
    }

    private static string TotalRow(JsonDocument rows) =>
        Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray().ToList()).GetProperty("values").GetRawText();

    private async Task<JsonDocument> GetAsync(string user, string path, HttpStatusCode expected)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request(path, HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        if (expected is HttpStatusCode.OK)
        {
            Assert.Equal(expected, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        }

        return await ReadProblemAsync(response, expected);
    }
}
