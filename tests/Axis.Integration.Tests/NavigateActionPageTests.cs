using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class NavigateActionPageTests(ProcessStartFixture fixture) : IClassFixture<ProcessStartFixture>
{
    private const string ShowSubmissions = """{"type":"navigate","page":"Submissions","labelKey":"request.showSubmissions","parameters":{},"record":null}""";

    private const string ShowSubmissionsOfRow = """{"type":"navigate","page":"Submissions","labelKey":"request.showSubmissions","parameters":{"request":"id"},"record":null}""";

    private const string OpenRequestForm = """{"type":"navigate","page":"RequestForm","labelKey":"pages.requestForm.title","parameters":{},"record":"id"}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_who_can_read_the_target_page_gets_the_navigate_actions_of_a_table()
    {
        var table = await TableOfOverviewAsync("anna");

        Assert.Equal([ShowSubmissionsOfRow, OpenRequestForm], RawItems(table.GetProperty("rowActions")));
        Assert.Equal([ShowSubmissions], RawItems(table.GetProperty("toolbarActions")));
        Assert.Equal(0, table.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task User_who_cannot_read_the_target_page_gets_a_table_without_navigate_actions()
    {
        var table = await TableOfOverviewAsync("olga");

        // Olga reads requests, so the form page stays. She cannot read the submissions page.
        Assert.Equal([OpenRequestForm], RawItems(table.GetProperty("rowActions")));
        Assert.Equal(0, table.GetProperty("toolbarActions").GetArrayLength());
    }

    [Fact]
    public async Task Form_widget_lists_its_start_process_and_navigate_actions_in_page_file_order()
    {
        var form = await FormOfRequestFormAsync("anna");

        Assert.Equal(
            [
                """{"type":"startProcess","process":"Submit","labelKey":"request.submit","instancesPath":"/api/apps/ProcessApp/processes/Submit/instances"}""",
                ShowSubmissions,
            ],
            RawItems(form.GetProperty("actions")));
        Assert.Equal(0, form.GetProperty("rowActions").GetArrayLength());
        Assert.Equal(0, form.GetProperty("toolbarActions").GetArrayLength());
    }

    [Fact]
    public async Task Form_widget_leaves_out_the_navigate_action_to_a_page_the_user_cannot_read()
    {
        var form = await FormOfRequestFormAsync("olga");

        // Olga holds no start grant either, so no action is left.
        Assert.Equal(0, form.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task Page_metadata_lists_the_parameters_of_the_page()
    {
        using var submissions = await GetAsync("anna", "/api/sites/processes/pages/submissions");
        using var overview = await GetAsync("anna", "/api/sites/processes/pages/overview");

        Assert.Equal(HttpStatusCode.OK, submissions.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var submissionsPage = JsonDocument.Parse(await submissions.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        var overviewPage = JsonDocument.Parse(await overview.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        Assert.Equal(
            ["""{"name":"request","type":"reference","required":false,"values":null,"target":{"entity":"Request","displayField":"title","recordsPath":"/api/apps/ProcessApp/entities/Request/records"}}"""],
            RawItems(submissionsPage.GetProperty("parameters")));
        Assert.Equal("[]", overviewPage.GetProperty("parameters").GetRawText());
    }

    private async Task<JsonElement> TableOfOverviewAsync(string user)
    {
        using var response = await GetAsync(user, "/api/sites/processes/pages/overview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        var columns = Assert.Single(page.GetProperty("widgets").EnumerateArray());
        var cell = columns.GetProperty("columns").EnumerateArray().First();
        return Assert.Single(cell.GetProperty("widgets").EnumerateArray());
    }

    private async Task<JsonElement> FormOfRequestFormAsync(string user)
    {
        using var response = await GetAsync(user, "/api/sites/processes/pages/requestform");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken)).RootElement;
        return Assert.Single(page.GetProperty("widgets").EnumerateArray());
    }

    private static string[] RawItems(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetRawText())];

    private async Task<HttpResponseMessage> GetAsync(string user, string path)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request(path, ProcessStartFixture.Host);
        return await client.SendAsync(request, CancellationToken);
    }
}
