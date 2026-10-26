using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class EnumLabelPageTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string ItemStatusRows = "/api/apps/RecordsApp/data-sources/ItemStatuses/rows";
    private const string StatusLabelKeys = """{"open":"item.status.open","closed":"item.status.closed"}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entity_field_with_value_labels_carries_the_label_key_of_each_value_and_others_carry_null()
    {
        using var page = await GetJsonAsync(fixture.Client, "/api/sites/records/pages/items");

        var fields = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList())
            .GetProperty("entity").GetProperty("fields").EnumerateArray()
            .ToDictionary(field => field.GetProperty("name").GetString()!);
        Assert.Equal(StatusLabelKeys, fields["status"].GetProperty("valueLabelKeys").GetRawText());
        Assert.Equal(JsonValueKind.Null, fields["name"].GetProperty("valueLabelKeys").ValueKind);
    }

    [Fact]
    public async Task Data_source_columns_and_parameters_carry_the_label_keys_of_their_enum_fields()
    {
        using var page = await GetJsonAsync(fixture.Client, "/api/sites/records/pages/itemstatuses");

        var dataSource = Assert.Single(page.RootElement.GetProperty("widgets").EnumerateArray().ToList())
            .GetProperty("dataSource");
        var columns = dataSource.GetProperty("columns").EnumerateArray()
            .ToDictionary(column => column.GetProperty("name").GetString()!);
        Assert.Equal(StatusLabelKeys, columns["status"].GetProperty("valueLabelKeys").GetRawText());
        Assert.Equal(JsonValueKind.Null, columns["name"].GetProperty("valueLabelKeys").ValueKind);

        var parameters = dataSource.GetProperty("parameters").EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);
        Assert.Equal(StatusLabelKeys, parameters["statusFilter"].GetProperty("valueLabelKeys").GetRawText());
        Assert.Equal(JsonValueKind.Null, parameters["nameFilter"].GetProperty("valueLabelKeys").ValueKind);
    }

    [Fact]
    public async Task Record_api_and_data_source_rows_return_the_raw_enum_value()
    {
        var name = $"Status {Guid.NewGuid():N}";
        using var create = Request(HttpMethod.Post, Items, HostA, $$"""{ "values": { "name": "{{name}}", "status": "open" } }""");
        using var created = await fixture.Client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var record = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = record.RootElement.GetProperty("id").GetGuid();

        using var get = Request($"{Items}/{id:D}", HostA);
        using var read = await fixture.Client.SendAsync(get, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var stored = JsonDocument.Parse(await read.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("open", stored.RootElement.GetProperty("values").GetProperty("status").GetString());

        using var rows = await GetJsonAsync(fixture.Client, $"{ItemStatusRows}?nameFilter={Uri.EscapeDataString(name)}");
        var row = Assert.Single(rows.RootElement.GetProperty("items").EnumerateArray().ToList());
        Assert.Equal("open", row.GetProperty("values").GetProperty("status").GetString());
    }

    [Fact]
    public async Task User_without_the_data_source_grant_cannot_read_the_rows_that_carry_the_labels()
    {
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var request = Request($"{ItemStatusRows}", HostA);
        using var response = await rita.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var request = Request(path, HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }
}
