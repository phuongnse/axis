using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class WidgetTreePageTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_who_reads_requests_and_inspects_the_process_gets_the_whole_tree()
    {
        using var response = await GetAsync("anna", "/api/sites/processes/pages/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = await ReadJsonAsync(response);
        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray());
        Assert.Equal(["type", "columns"], Properties(columns));
        Assert.Equal("columns", columns.GetProperty("type").GetString());
        var cells = columns.GetProperty("columns").EnumerateArray().ToArray();
        Assert.Equal([8, 4], cells.Select(cell => cell.GetProperty("width").GetInt32()));
        var table = Assert.Single(cells[0].GetProperty("widgets").EnumerateArray());
        Assert.Equal("table", table.GetProperty("type").GetString());
        Assert.Equal("Request", table.GetProperty("entity").GetProperty("name").GetString());
        var section = Assert.Single(cells[1].GetProperty("widgets").EnumerateArray());
        Assert.Equal(["type", "titleKey", "collapsed", "widgets"], Properties(section));
        Assert.Equal("section", section.GetProperty("type").GetString());
        Assert.Equal("pages.overview.inspector", section.GetProperty("titleKey").GetString());
        Assert.False(section.GetProperty("collapsed").GetBoolean());
        Assert.Equal(
            """{"type":"processInspector","entity":null,"dataSource":null,"form":null,"tasksPath":null,"process":"Submit","instancesPath":"/api/apps/ProcessApp/processes/Submit/instances","actions":[],"rowActions":[],"toolbarActions":[],"parameters":{},"access":null,"kpi":null}""",
            Assert.Single(section.GetProperty("widgets").EnumerateArray()).GetRawText());
    }

    [Fact]
    public async Task User_who_only_reads_requests_gets_the_page_without_the_unreadable_column()
    {
        using var response = await GetAsync("olga", "/api/sites/processes/pages/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = await ReadJsonAsync(response);
        var columns = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray());
        var cell = Assert.Single(columns.GetProperty("columns").EnumerateArray());
        Assert.Equal(8, cell.GetProperty("width").GetInt32());
        var table = Assert.Single(cell.GetProperty("widgets").EnumerateArray());
        Assert.Equal("table", table.GetProperty("type").GetString());
        Assert.Contains("read", table.GetProperty("access").GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
    }

    [Fact]
    public async Task Page_whose_widgets_are_all_unreadable_answers_403()
    {
        using var response = await GetAsync("olga", "/api/sites/processes/pages/submissions");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    private static string[] Properties(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request(path, ProcessStartFixture.Host);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
