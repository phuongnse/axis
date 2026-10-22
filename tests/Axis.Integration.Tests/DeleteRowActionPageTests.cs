using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class DeleteRowActionPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private const string DeleteItem = """{"type":"delete","labelKey":"items.delete","recordsPath":"/api/apps/RecordsApp/entities/Item/records","visibleWhen":null}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_with_the_delete_grant_gets_the_delete_action_with_the_records_path_of_the_entity()
    {
        var table = await TableOfPageAsync("anna", "items");

        Assert.Contains(DeleteItem, RawItems(table.GetProperty("rowActions")));
        Assert.DoesNotContain(DeleteItem, RawItems(table.GetProperty("toolbarActions")));
        Assert.Equal(0, table.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public async Task Table_over_a_data_source_carries_the_records_path_of_the_root_entity()
    {
        var table = await TableOfPageAsync("anna", "itemsbydepartment");

        Assert.Contains(DeleteItem, RawItems(table.GetProperty("rowActions")));
    }

    [Fact]
    public async Task User_without_the_delete_grant_gets_no_delete_action_and_keeps_the_others()
    {
        var table = await TableOfPageAsync("rita", "items");

        var rowActions = RawItems(table.GetProperty("rowActions"));
        Assert.DoesNotContain(DeleteItem, rowActions);
        Assert.Single(rowActions);
        Assert.Contains("\"type\":\"navigate\"", rowActions[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Direct_delete_by_a_user_without_the_grant_is_a_403_and_keeps_the_record()
    {
        await fixture.ResetAsync();
        using var create = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""");
        using var created = await fixture.Client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var record = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = record.RootElement.GetProperty("id").GetGuid();

        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var delete = Request(HttpMethod.Delete, $"{Items}/{id:D}", HostA);
        using var denied = await rita.SendAsync(delete, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var get = Request($"{Items}/{id:D}", HostA);
        using var read = await fixture.Client.SendAsync(get, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    private static string[] RawItems(JsonElement array) => [.. array.EnumerateArray().Select(item => item.GetRawText())];

    private async Task<JsonElement> TableOfPageAsync(string user, string page)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = Request($"/api/sites/records/pages/{page}", HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return Assert.Single(document.RootElement.GetProperty("widgets").EnumerateArray().ToList()).Clone();
    }
}
