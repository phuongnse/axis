using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class NavigatePathPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string ShowDepartmentItems = """{"type":"navigate","page":"ItemsInDepartment","labelKey":"itemLinks.sameDepartment","parameters":{"departmentName":"department.name"},"record":null,"opensForm":false,"paths":{"department":"/api/apps/RecordsApp/entities/Department/records"},"visibleWhen":null}""";

    private const string OpenItemForm = """{"type":"navigate","page":"ItemForm","labelKey":"itemLinks.open","parameters":{},"record":"id","opensForm":true,"paths":{},"visibleWhen":null}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_who_reads_the_departments_gets_the_path_action_with_the_record_api_of_each_reference()
    {
        using var page = await GetPageAsync("anna", "itemlinks");

        var table = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal(
            [ShowDepartmentItems, OpenItemForm],
            table.GetProperty("rowActions").EnumerateArray().Select(action => action.GetRawText()));
    }

    [Fact]
    public async Task User_who_cannot_read_the_departments_gets_the_table_without_the_path_action()
    {
        // The fielded role reads the item's name and department, but holds no grant on departments.
        using var page = await GetPageAsync("fia", "itemlinks");

        var table = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList());
        Assert.Equal([OpenItemForm], table.GetProperty("rowActions").EnumerateArray().Select(action => action.GetRawText()));
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
