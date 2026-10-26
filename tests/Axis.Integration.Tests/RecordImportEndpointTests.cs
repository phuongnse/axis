using System.Net;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Data.Records;
using Axis.Data.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordImportEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item";
    private const string Tickets = "/api/apps/RecordsApp/entities/Ticket";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_with_bad_rows_fails_its_dry_run_with_errors_by_row_and_field_and_writes_nothing_and_the_fixed_file_is_checked()
    {
        await ResetAsync();
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        var bad = "name,Quantity,price\nDesk,1,10.50\nChair,abc,5\nLamp,2,3\nStool,3,4\nTable,-1,9\n";

        var uploaded = await UploadAsync(fixture.Client, Items, bad);

        Assert.Equal(HttpStatusCode.Accepted, uploaded.Response.StatusCode);
        Assert.Equal($"{Items}/imports/{uploaded.Id:D}", uploaded.Response.Headers.Location!.ToString());
        Assert.Equal("pending", uploaded.Body.RootElement.GetProperty("state").GetString());
        Assert.Equal(1, await RunChecksAsync());

        using var failed = await GetAsync(fixture.Client, Items, uploaded.Id);

        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            ["/rows/2/values/quantity", "/rows/5/values/quantity"],
            failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await ItemCountAsync());
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        Assert.True(await FileClearedAsync(uploaded.Id));

        var fixedFile = "name,Quantity,price\nDesk,1,10.50\nChair,4,5\nLamp,2,3\nStool,3,4\nTable,1,9\n";
        var second = await UploadAsync(fixture.Client, Items, fixedFile);
        await RunChecksAsync();
        using var checkedImport = await GetAsync(fixture.Client, Items, second.Id);

        Assert.Equal("checked", checkedImport.RootElement.GetProperty("state").GetString());
        Assert.Empty(checkedImport.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(0L, await ItemCountAsync());
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        Assert.False(await FileClearedAsync(second.Id));
    }

    [Fact]
    public async Task An_xlsx_file_with_bad_rows_fails_its_dry_run_with_errors_by_sheet_row_and_the_fixed_file_is_checked()
    {
        await ResetAsync();
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        var bad = XlsxFiles.Rows(["name", "Quantity", "price"], ["Desk", "1", "10.50"], ["Chair", "abc", "5"], ["Lamp", "2", "3"], ["Stool", "3", "4"], ["Table", "-1", "9"]);

        var uploaded = await UploadXlsxAsync(fixture.Client, Items, bad);

        Assert.Equal(HttpStatusCode.Accepted, uploaded.Response.StatusCode);
        Assert.Equal(1, await RunChecksAsync());

        using var failed = await GetAsync(fixture.Client, Items, uploaded.Id);

        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            ["/rows/2/values/quantity", "/rows/5/values/quantity"],
            failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await ItemCountAsync());
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        Assert.True(await FileClearedAsync(uploaded.Id));

        var fixedFile = XlsxFiles.Rows(["name", "Quantity", "price"], ["Desk", "1", "10.50"], ["Chair", "4", "5"], ["Lamp", "2", "3"], ["Stool", "3", "4"], ["Table", "1", "9"]);
        var second = await UploadXlsxAsync(fixture.Client, Items, fixedFile);
        await RunChecksAsync();
        using var checkedImport = await GetAsync(fixture.Client, Items, second.Id);

        Assert.Equal("checked", checkedImport.RootElement.GetProperty("state").GetString());
        Assert.Empty(checkedImport.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(0L, await ItemCountAsync());
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        Assert.False(await FileClearedAsync(second.Id));
    }

    [Fact]
    public async Task A_body_that_is_not_a_workbook_is_accepted_and_fails_its_dry_run_at_the_columns()
    {
        await ResetAsync();

        var upload = await UploadXlsxAsync(fixture.Client, Items, "name,quantity\nDesk,1\n"u8.ToArray());
        await RunChecksAsync();

        Assert.Equal(["/columns"], await ErrorKeysAsync(upload.Id));
        Assert.Equal("xlsx", await fixture.ScalarAsync(TenantA, $"SELECT format FROM axis.record_imports WHERE id = '{upload.Id:D}'"));
    }

    [Fact]
    public async Task A_dry_run_applies_validations_defaults_and_required_fields_and_reports_header_problems_by_column()
    {
        await ResetAsync();

        var validation = await UploadAsync(fixture.Client, Items, "name,quantity,status,active\nDesk,1,closed,true\n,2,open,false\nLamp,2,open,maybe\n");
        var header = await UploadAsync(fixture.Client, Items, "name,nothing,NAME,parts\nDesk,1,2,3\n");
        var empty = await UploadAsync(fixture.Client, Items, "");
        var shape = await UploadAsync(fixture.Client, Items, "name,quantity\nDesk\nChair,\"1\n");
        await RunChecksAsync();

        Assert.Equal(["/rows/1/values/status", "/rows/2/values/name", "/rows/3/values/active"], await ErrorKeysAsync(validation.Id));
        Assert.Equal(["/columns/2", "/columns/3", "/columns/4"], await ErrorKeysAsync(header.Id));
        Assert.Equal(["/columns"], await ErrorKeysAsync(empty.Id));
        Assert.Equal(["/rows/1", "/rows/2"], await ErrorKeysAsync(shape.Id));
    }

    [Fact]
    public async Task A_file_with_more_rows_than_the_limit_fails_its_dry_run()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Items, "name\nA\nB\nC\nD\n");

        await RunChecksAsync(maxRows: 3);

        Assert.Equal(["/rows"], await ErrorKeysAsync(upload.Id));
        Assert.Equal("failed", await StateAsync(upload.Id));

        var atLimit = await UploadAsync(fixture.Client, Items, "name\nA\nB\nC\n");
        await RunChecksAsync(maxRows: 3);
        Assert.Equal("checked", await StateAsync(atLimit.Id));
    }

    [Fact]
    public async Task A_column_outside_the_users_create_grant_fails_the_dry_run_at_that_field()
    {
        await ResetAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        var upload = await UploadAsync(fia, Items, "name,quantity,price\nDesk,1,10\nChair,2,\n");
        await RunChecksAsync();

        using var failed = await GetAsync(fia, Items, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(["/rows/1/values/price"], failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));

        var allowed = await UploadAsync(fia, Items, "name,quantity\nDesk,1\n");
        await RunChecksAsync();
        using var checkedImport = await GetAsync(fia, Items, allowed.Id);
        Assert.Equal("checked", checkedImport.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task An_upload_without_the_import_grant_or_with_a_key_and_no_update_grant_is_a_403()
    {
        await ResetAsync();
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var fia = fixture.CreateClient().SignedInAs("fia");
        using var binh = fixture.CreateClient().SignedInAs("binh");

        using var noImport = await SendAsync(rita, HttpMethod.Post, $"{Items}/imports", "name\nDesk\n");
        using var noUpdate = await SendAsync(fia, HttpMethod.Post, $"{Tickets}/imports?key=title", "title\nA\n");
        using var noGrant = await SendAsync(binh, HttpMethod.Post, $"{Items}/imports", "name\nDesk\n");
        using var anonymous = fixture.CreateClient();
        using var anon = await SendAsync(anonymous, HttpMethod.Post, $"{Items}/imports", "name\nDesk\n");

        await ReadProblemAsync(noImport, HttpStatusCode.Forbidden);
        await ReadProblemAsync(noUpdate, HttpStatusCode.Forbidden);
        await ReadProblemAsync(noGrant, HttpStatusCode.Forbidden);
        await ReadProblemAsync(anon, HttpStatusCode.Unauthorized);
        Assert.Equal(0L, await ImportCountAsync());
    }

    [Fact]
    public async Task An_upload_with_a_bad_key_a_bad_content_type_or_a_bad_path_is_refused_without_storing_an_import()
    {
        await ResetAsync();

        using var badKey = await SendAsync(fixture.Client, HttpMethod.Post, $"{Items}/imports?key=name", "name\nDesk\n");
        using var unknownKey = await SendAsync(fixture.Client, HttpMethod.Post, $"{Tickets}/imports?key=nothing", "title\nA\n");
        using var json = await SendAsync(fixture.Client, HttpMethod.Post, $"{Items}/imports", "name\nDesk\n", "application/json");
        using var workbookWithParameter = await SendBytesAsync(fixture.Client, $"{Items}/imports", XlsxFiles.Rows(["name"], ["Desk"]), $"{XlsxFiles.ContentType}; charset=utf-8");
        using var workbook = await SendBytesAsync(fixture.Client, $"{Items}/imports", XlsxFiles.Rows(["name"], ["Desk"]), XlsxFiles.ContentType);
        using var noEntity = await SendAsync(fixture.Client, HttpMethod.Post, "/api/apps/RecordsApp/entities/Nothing/imports", "name\nDesk\n");
        using var goodKey = await SendAsync(fixture.Client, HttpMethod.Post, $"{Tickets}/imports?key=CODE", "title,code\nA,x\n");

        using (var problem = await ReadProblemAsync(badKey, HttpStatusCode.BadRequest))
        {
            Assert.Equal(["key"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        }

        await ReadProblemAsync(unknownKey, HttpStatusCode.BadRequest);
        await ReadProblemAsync(json, HttpStatusCode.UnsupportedMediaType);
        await ReadProblemAsync(workbookWithParameter, HttpStatusCode.UnsupportedMediaType);
        Assert.Equal(HttpStatusCode.Accepted, workbook.StatusCode);
        await ReadProblemAsync(noEntity, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.Accepted, goodKey.StatusCode);
        Assert.Equal(2L, await ImportCountAsync());
    }

    [Fact]
    public async Task Another_user_reading_an_import_gets_a_404_as_does_an_unknown_id()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Items, "name\nDesk\n");
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using var other = await SendAsync(fia, HttpMethod.Get, $"{Items}/imports/{upload.Id:D}", null);
        using var unknown = await SendAsync(fia, HttpMethod.Get, $"{Items}/imports/{Guid.NewGuid():D}", null);
        using var malformed = await SendAsync(fixture.Client, HttpMethod.Get, $"{Items}/imports/nothing", null);
        using var wrongEntity = await SendAsync(fixture.Client, HttpMethod.Get, $"{Tickets}/imports/{upload.Id:D}", null);
        using var own = await SendAsync(fixture.Client, HttpMethod.Get, $"{Items}/imports/{upload.Id:D}", null);

        await ReadProblemAsync(other, HttpStatusCode.NotFound);
        await ReadProblemAsync(unknown, HttpStatusCode.NotFound);
        await ReadProblemAsync(malformed, HttpStatusCode.NotFound);
        await ReadProblemAsync(wrongEntity, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task A_file_above_the_byte_limit_is_a_413_and_stores_nothing()
    {
        await ResetAsync();
        await using var limited = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Data:ImportMaxBytes", "64"));
        using var client = limited.CreateClient().SignedInAs("anna");
        var small = "name\nDesk\n";
        var large = "name\n" + string.Join('\n', Enumerable.Repeat("A long item name for the file", 10)) + "\n";

        using var tooLarge = await SendAsync(client, HttpMethod.Post, $"{Items}/imports", large);
        using var fits = await SendAsync(client, HttpMethod.Post, $"{Items}/imports", small);

        await ReadProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal(HttpStatusCode.Accepted, fits.StatusCode);
        Assert.Equal(1L, await ImportCountAsync());
    }

    private static async Task<(HttpResponseMessage Response, JsonDocument Body, Guid Id)> UploadAsync(HttpClient client, string path, string csv)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"{path}/imports", csv);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (response, body, body.RootElement.GetProperty("id").GetGuid());
    }

    private static async Task<(HttpResponseMessage Response, JsonDocument Body, Guid Id)> UploadXlsxAsync(HttpClient client, string path, byte[] workbook)
    {
        var response = await SendBytesAsync(client, $"{path}/imports", workbook, XlsxFiles.ContentType);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (response, body, body.RootElement.GetProperty("id").GetGuid());
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path, Guid id)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{path}/imports/{id:D}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body, string contentType = "text/csv") =>
        client.SendAsync(Request(method, path, HostA, body, contentType), CancellationToken);

    private static Task<HttpResponseMessage> SendBytesAsync(HttpClient client, string path, byte[] body, string contentType)
    {
        var request = Request(HttpMethod.Post, path, HostA, null, null);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return client.SendAsync(request, CancellationToken);
    }

    private async Task<List<string>> ErrorKeysAsync(Guid id)
    {
        using var import = await GetAsync(fixture.Client, Items, id);
        return [.. import.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name)];
    }

    // The imports and their work items of earlier tests are removed, so a test runs only its own dry runs.
    private async Task ResetAsync()
    {
        await fixture.ResetAsync();
        await fixture.ExecuteAsync(TenantA, "DELETE FROM axis.record_imports");
        await fixture.ExecuteAsync(TenantA, $"DELETE FROM axis.process_work_items WHERE kind = '{RecordImports.CheckWorkItemKind}'");
    }

    private async Task<string> StateAsync(Guid id)
    {
        using var import = await GetAsync(fixture.Client, Items, id);
        return import.RootElement.GetProperty("state").GetString()!;
    }

    /// <summary>Claims and runs every dry run the tenant has queued, and returns how many ran.</summary>
    private async Task<int> RunChecksAsync(int maxRows = 10000)
    {
        var handler = new RecordImportCheckHandler(new ReleaseModelCache(PackageCatalog.Empty), new RecordImportSettings(maxRows), NullLogger<RecordImportCheckHandler>.Instance);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var ran = 0;
        while (await WorkItemQueue.ClaimAsync(connection, TenantA, [RecordImports.CheckWorkItemKind], TimeSpan.FromMinutes(5), CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await handler.ExecuteAsync(item, connection, CancellationToken));
            ran++;
        }

        return ran;
    }

    private async Task<long> ItemCountAsync() =>
        (await fixture.TableShapeAsync(TenantA, "Item")).RowCount;

    private async Task<long> ImportCountAsync() =>
        (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.record_imports"))!;

    private async Task<bool> FileClearedAsync(Guid id) =>
        (bool)(await fixture.ScalarAsync(TenantA, $"SELECT file IS NULL FROM axis.record_imports WHERE id = '{id:D}'"))!;
}
