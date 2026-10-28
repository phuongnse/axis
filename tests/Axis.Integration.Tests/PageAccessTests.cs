using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

public sealed class PageAccessTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Site = "/api/sites/purchasing";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_employee_may_create_update_and_submit_a_purchase_request()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");

        var widget = await FormWidgetAsync(requester);

        var access = widget.GetProperty("access");
        Assert.Equal(["read", "create", "update"], Names(access.GetProperty("actions")));
        List<string?> fields = ["title", "department", "supplier", "currency", "lineItems", "status"];
        Assert.Equal(fields, Names(access.GetProperty("createFields")));
        Assert.Equal(fields, Names(access.GetProperty("updateFields")));
        var action = Assert.Single(widget.GetProperty("actions").EnumerateArray().ToList());
        Assert.Equal("PurchaseRequestApproval", action.GetProperty("process").GetString());
    }

    [Fact]
    public async Task A_department_manager_may_only_read_a_purchase_request_and_has_no_submit_action()
    {
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");

        var widget = await FormWidgetAsync(manager2);

        Assert.Equal("""{"actions":["read"],"createFields":[],"updateFields":[]}""", widget.GetProperty("access").GetRawText());
        Assert.Equal(0, widget.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task A_department_manager_keeps_the_task_inbox_in_navigation()
    {
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        using var request = RecordApiFixture.Request(Site, Host);

        using var response = await manager2.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var site = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Contains("MyTasks", Names(site.RootElement.GetProperty("navigation"), "page"));
    }

    private static async Task<JsonElement> FormWidgetAsync(HttpClient client)
    {
        using var request = RecordApiFixture.Request($"{Site}/pages/purchaserequestform", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList()).Clone();
    }

    private static List<string?> Names(JsonElement array, string? property = null) =>
        [.. array.EnumerateArray().Select(item => (property is null ? item : item.GetProperty(property)).GetString())];
}
