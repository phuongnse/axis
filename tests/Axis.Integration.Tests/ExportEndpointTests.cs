using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An export writes what the list and the rows routes show to the user, under the same grants, read
/// filters and field lists, and needs the <c>export</c> grant too. <c>eve</c> may read and export the items
/// that have a status, without their price, active, dates and total, and export ItemCountsByDepartment.
/// <c>rita</c> may read items but not export them.
/// </summary>
public sealed class ExportEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string CountsByDepartment = "/api/apps/RecordsApp/data-sources/ItemCountsByDepartment";
    private const string Searchable = "/api/apps/RecordsApp/data-sources/SearchableItems";
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task An_entity_export_holds_what_the_list_shows_with_the_hidden_rows_and_columns_absent_and_is_audited(string format)
    {
        await SeedAsync();
        var before = await AuditCountAsync();
        using var client = fixture.CreateClient().SignedInAs("eve");

        using var list = await GetJsonAsync(client, $"{Items}?pageSize=100");
        using var response = await GetAsync(client, $"{Items}/export?format={format}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDownload(response, format, "Item");
        var file = await ReadAsync(response, format);
        var shown = list.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, shown.Count);

        // The list shows these fields to eve, in declaration order. The price, the dates and the total are hidden, and so is the child collection.
        string[] columns = ["name", "quantity", "status", "department"];
        Assert.Equal(columns, file.Rows[0]);
        Assert.Equal(
            [.. shown.Select(item => Expected(item, columns, format))],
            file.Rows.Skip(1).ToList());
        Assert.DoesNotContain(file.Rows.Skip(1), row => row.Contains("Gamma"));
        if (format == "xlsx")
        {
            // A number is a number cell with its digits, and text that looks like a formula is still text.
            Assert.All(file.Cells!.Skip(1), row =>
            {
                Assert.Equal(CellValues.Number, row[1]!.DataType!.Value);
                Assert.Equal(CellValues.InlineString, row[0]!.DataType!.Value);
                Assert.All(row, cell => Assert.Null(cell!.CellFormula));
            });
        }

        Assert.Equal(before + 1, await AuditCountAsync());
        var audit = await LatestAuditAsync();
        Assert.Equal("eve", audit.Actor);
        Assert.Equal("Eve Exporter", audit.ActorName);
        Assert.Equal(EntityId("Item"), audit.EntityId);
        Assert.Equal($$"""{"format": "{{format}}", "rowCount": 3, "truncated": false}""", audit.Details);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task A_data_source_export_holds_the_groups_the_rows_route_shows_and_is_audited(string format)
    {
        await SeedAsync();
        var before = await AuditCountAsync();
        using var client = fixture.CreateClient().SignedInAs("eve");

        using var rows = await GetJsonAsync(client, $"{CountsByDepartment}/rows?pageSize=100");
        using var response = await GetAsync(client, $"{CountsByDepartment}/export?format={format}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDownload(response, format, "ItemCountsByDepartment");
        var file = await ReadAsync(response, format);
        var shown = rows.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Gamma has no status, so the read filter hides it: Sales has one item. The group fields come before the measures.
        string[] columns = ["departmentName", "items", "quantitySum"];
        Assert.Equal(3, shown.Count);
        Assert.Equal(columns, file.Rows[0]);
        Assert.Equal(
            [.. shown.Select(row => Expected(row, columns, format))],
            file.Rows.Skip(1).ToList());
        Assert.Equal(["Ops", "1", "2"], file.Rows[1]);
        Assert.Equal(["Sales", "1", "3"], file.Rows[2]);
        Assert.Equal([null, "1", "-7"], file.Rows[3]);

        Assert.Equal(before + 1, await AuditCountAsync());
        var audit = await LatestAuditAsync();
        Assert.Equal("eve", audit.Actor);
        Assert.Null(audit.EntityId);
        Assert.Equal(
            $$"""{"format": "{{format}}", "rowCount": 3, "truncated": false, "dataSourceId": "{{DataSourceId("ItemCountsByDepartment")}}"}""",
            audit.Details);
    }

    [Fact]
    public async Task A_user_with_read_but_no_export_gets_403_on_both_routes_and_nothing_is_audited()
    {
        await SeedAsync();
        var before = await AuditCountAsync();
        using var client = fixture.CreateClient().SignedInAs("rita");

        // The grant comes before the query, so a bad format is a 403 too.
        foreach (var path in new[] { $"{Items}/export?format=csv", $"{Items}/export?format=nope", $"{CountsByDepartment}/export?format=xlsx" })
        {
            using var response = await GetAsync(client, path);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Rita does read the items, so the missing grant is the export one.
        using var list = await GetJsonAsync(client, Items);
        Assert.NotEmpty(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(before, await AuditCountAsync());
    }

    [Fact]
    public async Task A_missing_or_unknown_format_is_a_400_at_format_after_the_grants_and_without_a_file()
    {
        await SeedAsync();
        var before = await AuditCountAsync();
        using var client = fixture.CreateClient().SignedInAs("eve");

        foreach (var path in new[] { $"{Items}/export", $"{Items}/export?format=CSV", $"{CountsByDepartment}/export?format=pdf" })
        {
            using var response = await GetAsync(client, path);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(["format"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        }

        Assert.Equal(before, await AuditCountAsync());
    }

    [Fact]
    public async Task A_request_without_a_session_is_a_401_and_an_unknown_name_a_404()
    {
        await SeedAsync();
        using var anonymous = fixture.CreateClient();
        using var eve = fixture.CreateClient().SignedInAs("eve");

        using var signedOut = await GetAsync(anonymous, $"{Items}/export?format=csv");
        using var unknownEntity = await GetAsync(eve, "/api/apps/RecordsApp/entities/Nothing/records/export?format=csv");
        using var unknownDataSource = await GetAsync(eve, "/api/apps/RecordsApp/data-sources/Nothing/export?format=csv");

        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownEntity.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownDataSource.StatusCode);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task An_export_past_the_row_limit_stops_with_complete_rows_and_says_so(string format)
    {
        await SeedAsync();
        var before = await AuditCountAsync();
        await using var host = CreateHost(maxRows: 2);
        using var client = host.CreateClient().SignedInAs("eve");

        using var response = await GetAsync(client, $"{Items}/export?format={format}");

        // Three items match eve's read filter.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["true"], response.Headers.GetValues("Axis-Export-Truncated"));
        var file = await ReadAsync(response, format);
        Assert.Equal(3, file.Rows.Count);
        Assert.All(file.Rows, row => Assert.Equal(4, row.Count));
        Assert.Equal(before + 1, await AuditCountAsync());
        Assert.Equal("""{"format": "{{format}}", "rowCount": 2, "truncated": true}""".Replace("{{format}}", format, StringComparison.Ordinal), (await LatestAuditAsync()).Details);
    }

    [Fact]
    public async Task An_export_of_exactly_the_row_limit_is_not_truncated()
    {
        await SeedAsync();
        await using var host = CreateHost(maxRows: 3);
        using var client = host.CreateClient().SignedInAs("eve");

        using var response = await GetAsync(client, $"{Items}/export?format=csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Axis-Export-Truncated"));
        Assert.Equal(4, (await ReadAsync(response, "csv")).Rows.Count);
    }

    [Fact]
    public async Task The_sort_of_the_list_applies_and_a_sort_by_a_hidden_field_is_a_400()
    {
        await SeedAsync();
        using var client = fixture.CreateClient().SignedInAs("eve");

        using var sorted = await GetAsync(client, $"{Items}/export?format=csv&sort=-quantity");
        using var hidden = await GetAsync(client, $"{Items}/export?format=csv&sort=price");

        Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);
        Assert.Equal(["Alpha", "Beta", "-minus"], (await ReadAsync(sorted, "csv")).Rows.Skip(1).Select(row => row[0]!.TrimStart('\'')).Select(name => name.TrimStart('=')));
        Assert.Equal(HttpStatusCode.BadRequest, hidden.StatusCode);
    }

    [Fact]
    public async Task A_data_source_export_with_search_writes_only_the_matching_rows_and_a_search_without_the_option_is_a_400_at_search()
    {
        await SeedAsync();
        using var client = fixture.CreateClient().SignedInAs("eve");

        using var all = await GetAsync(client, $"{Searchable}/export?format=csv");
        using var searched = await GetAsync(client, $"{Searchable}/export?format=csv&search=sal");
        using var none = await GetAsync(client, $"{Searchable}/export?format=csv&search=nothing");
        using var unsupported = await GetAsync(client, $"{CountsByDepartment}/export?format=csv&search=x");
        using var repeated = await GetAsync(client, $"{Searchable}/export?format=csv&search=a&search=b");

        // Gamma has no status, so eve's read filter hides it. Alpha is the only item of Sales that eve reads, so the header and three rows are left.
        Assert.Equal(4, (await ReadAsync(all, "csv")).Rows.Count);
        Assert.Equal([["Alpha", "Sales", "3"]], (await ReadAsync(searched, "csv")).Rows.Skip(1));
        Assert.Single((await ReadAsync(none, "csv")).Rows);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        using var problem = JsonDocument.Parse(await unsupported.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["search"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name));
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
    }

    // Departments Sales and Ops, and four items. Gamma has no status, so eve's read filter hides it.
    // Item names that start with a formula character and a negative quantity show how a CSV treats text and numbers.
    private async Task SeedAsync()
    {
        await fixture.ResetAsync();
        var sales = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Sales" });
        var ops = await fixture.InsertAsync(TenantA, "Department", new Dictionary<string, string?> { ["name"] = "Ops" });
        (string Name, string? Quantity, string? Status, Guid? Department)[] items =
        [
            ("Alpha", "3", "open", sales),
            ("=Beta", "2", "closed", ops),
            ("Gamma", "1", null, sales),
            ("-minus", "-7", "open", null),
        ];
        foreach (var (name, quantity, status, department) in items)
        {
            await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?>
            {
                ["name"] = name,
                ["quantity"] = quantity,
                ["price"] = "9.99",
                ["status"] = status,
                ["department"] = department?.ToString(),
            });
        }
    }

    // The cells of the list or rows entry of one row, as the file holds them: the label of a reference, a number as it is
    // written, and in a CSV a text cell that starts with a formula character with a leading quote.
    private static List<string?> Expected(JsonElement item, string[] columns, string format)
    {
        var values = item.GetProperty("values");
        var labels = item.GetProperty("labels");
        return [.. columns.Select(name =>
        {
            var value = values.GetProperty(name);
            var text = value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => labels.TryGetProperty(name, out var label) ? label.GetString() : value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new InvalidOperationException($"No cell form for {value.ValueKind}."),
            };
            return format == "csv" && value.ValueKind == JsonValueKind.String && text is [('=' or '+' or '-' or '@' or '\t' or '\r'), ..] ? "'" + text : text;
        })];
    }

    private static void AssertDownload(HttpResponseMessage response, string format, string name)
    {
        Assert.Equal(format == "csv" ? "text/csv" : XlsxContentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal($"{name}.{format}", response.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.False(response.Headers.Contains("Axis-Export-Truncated"));
    }

    private static async Task<ExportedFile> ReadAsync(HttpResponseMessage response, string format)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        if (format == "csv")
        {
            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            var csv = CsvText.Parse(Encoding.UTF8.GetString(bytes[3..]));
            return new ExportedFile([.. csv.Select(row => row.Select(cell => cell.Length == 0 ? null : cell).ToList())], null);
        }

        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheet = Assert.Single(workbook.Workbook!.Sheets!.Elements<Sheet>());
        var data = ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!.GetFirstChild<SheetData>()!;
        var cells = data.Elements<Row>().Select(row => row.Elements<Cell>().ToList()).ToList();
        var rows = cells.Select(row => row.Select(CellText).ToList()).ToList();
        return new ExportedFile(rows, [.. cells.Select(row => row.Select(cell => (Cell?)cell).ToList())]);
    }

    private static string? CellText(Cell cell) =>
        cell.DataType?.Value == CellValues.InlineString ? cell.InlineString!.Text!.Text : cell.CellValue?.Text;

    private sealed record ExportedFile(List<List<string?>> Rows, List<List<Cell?>>? Cells);

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path)
    {
        using var request = Request(path, HostA);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await GetAsync(client, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private WebApplicationFactory<Program> CreateHost(int maxRows) =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Data:ExportMaxRows", maxRows.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private Guid EntityId(string name)
    {
        Assert.True(fixture.Model.TryGetEntity(name, out var entity));
        return entity.Id;
    }

    private Guid DataSourceId(string name)
    {
        Assert.True(fixture.Model.TryGetDataSource(name, out var dataSource));
        return dataSource.Id;
    }

    private async Task<long> AuditCountAsync() =>
        (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.audit_records WHERE action = 'records.exported'"))!;

    private async Task<(string Actor, string? ActorName, Guid? EntityId, string Details)> LatestAuditAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT actor, actor_name, entity_id, details::text FROM axis.audit_records WHERE action = 'records.exported' ORDER BY occurred_at DESC, id DESC LIMIT 1",
            connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.GetString(3));
    }
}
