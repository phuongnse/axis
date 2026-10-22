using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class ProcessInspectorPageTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Inspector_widget_describes_its_process_and_list_endpoint_and_has_no_access()
    {
        using var response = await GetAsync("anna", "/api/sites/processes/pages/submissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = await ReadJsonAsync(response);
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray());
        Assert.Equal(
            """{"type":"processInspector","formPage":null,"entity":null,"dataSource":null,"form":null,"tasksPath":null,"process":"Submit","instancesPath":"/api/apps/ProcessApp/processes/Submit/instances","actions":[],"rowActions":[],"toolbarActions":[],"parameters":{},"access":null}""",
            widget.GetRawText());
    }

    [Fact]
    public async Task User_with_inspect_sees_the_inspector_page_in_navigation_and_reads_it()
    {
        using var response = await GetAsync("anna", "/api/sites/processes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var site = await ReadJsonAsync(response);
        Assert.Equal(["Requests", "Submissions"], NavigationPages(site));
    }

    [Fact]
    public async Task User_without_inspect_does_not_see_the_inspector_page_and_gets_a_403_for_it()
    {
        using var navigation = await GetAsync("olga", "/api/sites/processes");
        using var page = await GetAsync("olga", "/api/sites/processes/pages/submissions");

        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        using var site = await ReadJsonAsync(navigation);
        Assert.Equal(["Requests"], NavigationPages(site));
        using var problem = await ReadProblemAsync(page, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Table_widget_has_no_process_and_no_instances_path()
    {
        using var response = await GetAsync("olga", "/api/sites/processes/pages/requests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = await ReadJsonAsync(response);
        var widget = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("process").ValueKind);
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("instancesPath").ValueKind);
    }

    private static string?[] NavigationPages(JsonDocument site) =>
        [.. site.RootElement.GetProperty("navigation").EnumerateArray().Select(entry => entry.GetProperty("page").GetString())];

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request(path, ProcessStartFixture.Host);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
