using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class DetailPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_with_full_access_gets_the_detail_with_its_record_parameter_form_and_access()
    {
        using var page = await GetPageAsync("anna", "orderdetail");

        Assert.Equal("OrderDetail", page.RootElement.GetProperty("name").GetString());
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("detail", widget.GetProperty("type").GetString());
        Assert.Equal("""{"record":"order"}""", widget.GetProperty("detail").GetRawText());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("dataSource").ValueKind);
        Assert.Equal("Order", widget.GetProperty("entity").GetProperty("name").GetString());
        Assert.Equal(4, widget.GetProperty("entity").GetProperty("fields").GetArrayLength());
        Assert.Equal("OrderSummary", widget.GetProperty("form").GetProperty("name").GetString());
        Assert.Equal(
            ["main", "lines"],
            widget.GetProperty("form").GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        Assert.Equal(JsonValueKind.Object, widget.GetProperty("access").ValueKind);
        Assert.Equal(0, widget.GetProperty("actions").GetArrayLength());
        Assert.Equal("detail", widget.EnumerateObject().SkipWhile(property => property.Name != "access").ElementAt(1).Name);
        Assert.Equal("chart", widget.EnumerateObject().Last().Name);

        var parameter = Assert.Single(page.RootElement.GetProperty("parameters").EnumerateArray().ToList());
        Assert.Equal(
            """{"name":"order","type":"reference","required":true,"values":null,"target":{"entity":"Order","displayField":"title","recordsPath":"/api/apps/RecordsApp/entities/Order/records"}}""",
            parameter.GetRawText());
    }

    [Fact]
    public async Task Widget_that_is_not_a_detail_has_a_null_detail()
    {
        using var page = await GetPageAsync("anna", "items");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task User_who_reads_only_some_fields_gets_a_detail_with_only_those_fields()
    {
        // Fia may read the line count of an order, and nothing else.
        using var page = await GetPageAsync("fia", "orderdetail");

        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal("""{"record":"order"}""", widget.GetProperty("detail").GetRawText());
        Assert.Equal(
            ["lineCount"],
            widget.GetProperty("entity").GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));

        // The lines section has no readable field left, so it is gone.
        var section = Assert.Single(widget.GetProperty("form").GetProperty("sections").EnumerateArray().ToList());
        Assert.Equal("main", section.GetProperty("name").GetString());
        Assert.Equal(
            ["lineCount"],
            section.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task User_with_no_read_grant_on_the_entity_gets_a_403_problem_for_a_page_with_only_a_detail()
    {
        // Dora holds a grant on data sources only, and none on Order.
        using var client = fixture.CreateClient().SignedInAs("dora");
        using var request = Request("/api/sites/records/pages/orderdetail", HostA);

        using var response = await client.SendAsync(request, CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Site_navigation_does_not_list_the_detail_page()
    {
        using var client = fixture.CreateClient().SignedInAs("anna");
        using var request = Request("/api/sites/records", HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var site = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

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
}
