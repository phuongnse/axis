using System.Net;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Data.Naming;
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
    private const string Products = "/api/apps/RecordsApp/entities/Product/records";

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
        using var client = limited.CreateClient().SignedInAs("requester");
        var small = "name\nDesk\n";
        var large = "name\n" + string.Join('\n', Enumerable.Repeat("A long item name for the file", 10)) + "\n";

        using var tooLarge = await SendAsync(client, HttpMethod.Post, $"{Items}/imports", large);
        using var fits = await SendAsync(client, HttpMethod.Post, $"{Items}/imports", small);

        await ReadProblemAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge);
        Assert.Equal(HttpStatusCode.Accepted, fits.StatusCode);
        Assert.Equal(1L, await ImportCountAsync());
    }

    [Fact]
    public async Task Committing_a_checked_import_creates_every_row_as_the_uploader_and_audits_the_import()
    {
        await ResetAsync();

        // Audit records are never deleted, so an earlier commit leaves its records.imported record behind. Committing one here
        // makes that leftover present on every run, whatever order the tests run in.
        var earlier = await UploadAsync(fixture.Client, Tickets, "title,code\nLamp,l1\n");
        await RunChecksAsync();
        using var queued = await CommitAsync(fixture.Client, Tickets, earlier.Id);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        Assert.Equal(1, await RunCommitsAsync());
        await ResetAsync();

        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\nScanner,s1\nDesk,d1\n");
        await RunChecksAsync();
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        var imported = await ImportedAuditCountAsync();
        var importedByAnna = await ImportedAuditCountAsync(ImportedByAnnaFilter);

        using var accepted = await CommitAsync(fixture.Client, Tickets, upload.Id);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal($"{Tickets}/imports/{upload.Id:D}", accepted.Headers.Location!.ToString());
        Assert.Equal("checked", await StateAsync(upload.Id, Tickets));
        Assert.Equal(0L, await TicketCountAsync());
        Assert.Equal(1, await RunCommitsAsync());

        Assert.Equal("committed", await StateAsync(upload.Id, Tickets));
        Assert.True(await FileClearedAsync(upload.Id));
        Assert.Equal(3L, await TicketCountAsync());
        Assert.Equal(3L, await TicketCountAsync("f_requester = 'requester' AND f_number IS NOT NULL"));
        Assert.Equal(3L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(DISTINCT f_number) FROM {TicketTable}"))!);
        Assert.Equal(
            3L,
            (long)(await fixture.ScalarAsync(
                TenantA,
                $"SELECT count(*) FROM axis.audit_records WHERE action = 'record.created' AND actor = 'requester' AND actor_name = 'Requester User' AND record_id IN (SELECT id FROM {TicketTable})"))!);
        Assert.Equal(audits + 4, await fixture.AuditRecordCountAsync(TenantA));
        Assert.Equal(imported + 1, await ImportedAuditCountAsync());
        Assert.Equal(importedByAnna + 1, await ImportedAuditCountAsync(ImportedByAnnaFilter));
        Assert.Equal(0L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CommitWorkItemKind}'"))!);
    }

    [Fact]
    public async Task A_keyed_commit_updates_the_records_whose_key_matches_creates_the_others_and_audits_both()
    {
        await ResetAsync();
        await CreateTicketAsync("a1", "Printer", 2);
        await CreateTicketAsync("b1", "Scanner", 1);
        var upload = await UploadAsync(fixture.Client, Tickets, "code,title,priority\na1,Renamed,\nb1,Other,3\nn1,New,\n,Blank,\n", key: "code");
        await RunChecksAsync();
        Assert.Equal("checked", await StateAsync(upload.Id, Tickets));
        var updates = (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.audit_records WHERE action = 'record.updated'"))!;

        using var accepted = await CommitAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, await RunCommitsAsync());

        Assert.Equal("committed", await StateAsync(upload.Id, Tickets));
        Assert.Equal(4L, await TicketCountAsync());
        Assert.Equal("Renamed", await TicketFieldAsync("a1", "f_title"));
        Assert.Equal(2L, await TicketFieldAsync("a1", "f_priority"));
        Assert.Equal("Other", await TicketFieldAsync("b1", "f_title"));
        Assert.Equal(3L, await TicketFieldAsync("b1", "f_priority"));
        Assert.Equal("New", await TicketFieldAsync("n1", "f_title"));
        Assert.Equal(1L, await TicketCountAsync("f_title = 'Blank' AND f_code IS NULL"));
        Assert.Equal(
            2L,
            (long)(await fixture.ScalarAsync(
                TenantA,
                $"SELECT count(*) FROM axis.audit_records WHERE action = 'record.updated' AND actor = 'requester' AND actor_name = 'Requester User' AND record_id IN (SELECT id FROM {TicketTable} WHERE f_code IN ('a1', 'b1'))"))!);
        Assert.Equal(updates + 2, (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.audit_records WHERE action = 'record.updated'"))!);
        Assert.Equal(
            1L,
            await ImportedAuditCountAsync("actor = 'requester' AND details = '{\"created\": 2, \"updated\": 2}'::jsonb"));
    }

    [Fact]
    public async Task A_keyed_row_outside_the_uploaders_update_filter_or_field_list_fails_the_dry_run_and_writes_nothing()
    {
        await ResetAsync();
        await CreateTicketAsync("p1", "First", 1);
        await CreateTicketAsync("p2", "Second", 2);
        using var kai = fixture.CreateClient().SignedInAs("kai");
        var refused = await UploadAsync(kai, Tickets, "code,title,priority\np2,X,\np1,Y,5\n", key: "code");
        await RunChecksAsync();

        using var failed = await GetAsync(kai, Tickets, refused.Id);

        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        var errors = failed.RootElement.GetProperty("errors");
        Assert.Equal(["/rows/1", "/rows/2/values/priority"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal("The record is outside the uploader's update filters.", errors.GetProperty("/rows/1")[0].GetString());
        Assert.Equal(2L, await TicketCountAsync());
        Assert.Equal("Second", await TicketFieldAsync("p2", "f_title"));
        Assert.Equal("First", await TicketFieldAsync("p1", "f_title"));

        var allowed = await UploadAsync(kai, Tickets, "code,title\np1,Y\n", key: "code");
        await RunChecksAsync();
        using (var checkedImport = await GetAsync(kai, Tickets, allowed.Id))
        {
            Assert.Equal("checked", checkedImport.RootElement.GetProperty("state").GetString());
        }

        using var accepted = await CommitAsync(kai, Tickets, allowed.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, await RunCommitsAsync());
        using (var committedImport = await GetAsync(kai, Tickets, allowed.Id))
        {
            Assert.Equal("committed", committedImport.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("Y", await TicketFieldAsync("p1", "f_title"));
        Assert.Equal(2L, await TicketCountAsync());
    }

    [Fact]
    public async Task A_keyed_file_with_no_column_for_the_key_fails_the_dry_run_at_the_columns()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title\nA\n", key: "code");
        await RunChecksAsync();

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);

        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(["/columns"], failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await TicketCountAsync());
    }

    [Fact]
    public async Task A_reference_cell_may_hold_the_targets_display_value_and_the_commit_links_the_records()
    {
        await ResetAsync();
        var desk = await CreateProductAsync("Desk");
        var lamp = await CreateProductAsync("Lamp");
        var chair = await CreateProductAsync("Chair");
        var upload = await UploadAsync(fixture.Client, Tickets, $"title,code,product\nA,a1,Desk\nB,b1,Lamp\nC,c1,{chair:D}\nD,d1,Desk\nE,e1,\n");
        await RunChecksAsync();
        Assert.Equal("checked", await StateAsync(upload.Id, Tickets));

        using var accepted = await CommitAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await RunCommitsAsync();

        Assert.Equal("committed", await StateAsync(upload.Id, Tickets));
        Assert.Equal(5L, await TicketCountAsync());
        Assert.Equal(desk, await ProductOfAsync("a1"));
        Assert.Equal(lamp, await ProductOfAsync("b1"));
        Assert.Equal(chair, await ProductOfAsync("c1"));
        Assert.Equal(desk, await ProductOfAsync("d1"));
        Assert.Null(await ProductOfAsync("e1"));
    }

    [Fact]
    public async Task A_display_value_that_matches_no_record_or_more_than_one_fails_the_dry_run_without_repeating_the_value()
    {
        await ResetAsync();
        await CreateProductAsync("Chair");
        await CreateProductAsync("Chair");
        await CreateProductAsync("Desk");
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code,product\nA,a1,Chair\nB,b1,Nothing\nC,c1,Desk\nD,d1,desk\n");

        await RunChecksAsync();

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        var errors = failed.RootElement.GetProperty("errors");
        Assert.Equal(
            ["/rows/1/values/product", "/rows/2/values/product", "/rows/4/values/product"],
            errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal("Matches more than one record by its display value.", errors.GetProperty("/rows/1/values/product")[0].GetString());
        Assert.Equal("Matches no record by its display value.", errors.GetProperty("/rows/2/values/product")[0].GetString());
        Assert.DoesNotContain("Chair", errors.GetRawText());
        Assert.DoesNotContain("Nothing", errors.GetRawText());
        Assert.Equal(0L, await TicketCountAsync());
    }

    [Fact]
    public async Task A_display_value_is_not_looked_up_for_an_uploader_who_may_not_read_the_targets_display_field()
    {
        await ResetAsync();
        await CreateProductAsync("Desk");
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code,product\nA,a1,Desk\n");
        await fixture.ExecuteAsync(TenantA, "UPDATE axis.record_imports SET uploader_roles = '{filtered}'");

        await RunChecksAsync();

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        var error = Assert.Single(failed.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("/rows/1/values/product", error.Name);
        Assert.DoesNotContain("display value", error.Value[0].GetString());
    }

    [Fact]
    public async Task A_row_that_fails_at_write_time_rolls_back_every_row_and_fails_the_import_with_that_rows_errors()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,x1\nScanner,x2\nDesk,x1\n");
        await RunChecksAsync();
        Assert.Equal("checked", await StateAsync(upload.Id, Tickets));
        var audits = await fixture.AuditRecordCountAsync(TenantA);

        using var accepted = await CommitAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, await RunCommitsAsync());

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal(["/rows/3/values/code"], failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await TicketCountAsync());
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        Assert.True(await FileClearedAsync(upload.Id));

        // The sequence numbers the failed rows took are handed back.
        var next = await UploadAsync(fixture.Client, Tickets, "title,code\nLamp,l1\n");
        await RunChecksAsync();
        using var again = await CommitAsync(fixture.Client, Tickets, next.Id);
        await RunCommitsAsync();
        Assert.Equal("committed", await StateAsync(next.Id, Tickets));
        Assert.Equal(1L, await TicketCountAsync());
    }

    [Fact]
    public async Task Another_user_or_a_user_without_the_grant_cannot_commit_the_import()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        await RunChecksAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");
        using var rita = fixture.CreateClient().SignedInAs("rita");
        using var anonymous = fixture.CreateClient();

        using var other = await CommitAsync(fia, Tickets, upload.Id);
        using var unknown = await CommitAsync(fixture.Client, Tickets, Guid.NewGuid());
        using var malformed = await SendAsync(fixture.Client, HttpMethod.Post, $"{Tickets}/imports/nothing/commit", null);
        using var wrongEntity = await CommitAsync(fixture.Client, Items, upload.Id);
        using var noGrant = await CommitAsync(rita, Tickets, upload.Id);
        using var noSession = await CommitAsync(anonymous, Tickets, upload.Id);

        await ReadProblemAsync(other, HttpStatusCode.NotFound);
        await ReadProblemAsync(unknown, HttpStatusCode.NotFound);
        await ReadProblemAsync(malformed, HttpStatusCode.NotFound);
        await ReadProblemAsync(wrongEntity, HttpStatusCode.NotFound);
        await ReadProblemAsync(noGrant, HttpStatusCode.Forbidden);
        await ReadProblemAsync(noSession, HttpStatusCode.Unauthorized);
        Assert.Equal("checked", await StateAsync(upload.Id, Tickets));
        Assert.Equal(0L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CommitWorkItemKind}'"))!);
        Assert.Equal(0, await RunCommitsAsync());
        Assert.Equal(0L, await TicketCountAsync());
    }

    [Fact]
    public async Task Only_a_checked_import_with_no_commit_queued_can_be_committed()
    {
        await ResetAsync();
        var pending = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        using var whilePending = await CommitAsync(fixture.Client, Tickets, pending.Id);
        await RunChecksAsync();
        var broken = await UploadAsync(fixture.Client, Tickets, "title,code\n,p1\n");
        await RunChecksAsync();
        using var whileFailed = await CommitAsync(fixture.Client, Tickets, broken.Id);

        await ReadProblemAsync(whilePending, HttpStatusCode.Conflict);
        await ReadProblemAsync(whileFailed, HttpStatusCode.Conflict);
        Assert.Equal("failed", await StateAsync(broken.Id, Tickets));

        using var first = await CommitAsync(fixture.Client, Tickets, pending.Id);
        using var second = await CommitAsync(fixture.Client, Tickets, pending.Id);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        await ReadProblemAsync(second, HttpStatusCode.Conflict);
        Assert.Equal(1L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CommitWorkItemKind}'"))!);
        Assert.Equal(1, await RunCommitsAsync());
        Assert.Equal(1L, await TicketCountAsync());

        using var afterCommit = await CommitAsync(fixture.Client, Tickets, pending.Id);
        await ReadProblemAsync(afterCommit, HttpStatusCode.Conflict);
        Assert.Equal(0, await RunCommitsAsync());
        Assert.Equal(1L, await TicketCountAsync());
    }

    [Fact]
    public async Task The_cleanup_clears_the_file_of_a_checked_import_older_than_24_hours_and_its_commit_is_a_409()
    {
        await ResetAsync();
        var old = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        var fresh = await UploadAsync(fixture.Client, Tickets, "title,code\nScanner,s1\n");
        Assert.Equal(2, await RunChecksAsync());
        Assert.Equal("checked", await StateAsync(old.Id, Tickets));
        Assert.Equal("checked", await StateAsync(fresh.Id, Tickets));
        await BackdateAsync(old.Id);

        Assert.Equal(1, await ClearExpiredFilesAsync());

        Assert.True(await FileClearedAsync(old.Id));
        Assert.Equal("checked", await StateAsync(old.Id, Tickets));
        Assert.False(await FileClearedAsync(fresh.Id));
        using var refused = await CommitAsync(fixture.Client, Tickets, old.Id);
        using var accepted = await CommitAsync(fixture.Client, Tickets, fresh.Id);

        using var problem = await ReadProblemAsync(refused, HttpStatusCode.Conflict);
        Assert.Equal("The file of this import was deleted.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CommitWorkItemKind}'"))!);
        Assert.Equal(1, await RunCommitsAsync());
        Assert.Equal(1L, await TicketCountAsync());
    }

    [Fact]
    public async Task The_cleanup_fails_a_pending_import_older_than_24_hours_with_an_error_at_the_empty_pointer()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        await BackdateAsync(upload.Id);

        Assert.Equal(1, await ClearExpiredFilesAsync());

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        var error = Assert.Single(failed.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal("", error.Name);
        Assert.Equal(RecordImports.FileDeletedError, error.Value[0].GetString());
        Assert.True(await FileClearedAsync(upload.Id));
        Assert.Equal(1, await RunChecksAsync());
        Assert.Equal("failed", await StateAsync(upload.Id, Tickets));
        Assert.Equal(0L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CheckWorkItemKind}'"))!);
    }

    [Fact]
    public async Task Another_user_committing_a_cleared_import_gets_a_404_not_a_409()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        await RunChecksAsync();
        await BackdateAsync(upload.Id);
        await ClearExpiredFilesAsync();
        using var fia = fixture.CreateClient().SignedInAs("fia");

        using var other = await CommitAsync(fia, Tickets, upload.Id);

        await ReadProblemAsync(other, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_commit_queued_before_the_cleanup_ends_failed_at_the_empty_pointer_and_writes_no_record()
    {
        await ResetAsync();
        var upload = await UploadAsync(fixture.Client, Tickets, "title,code\nPrinter,p1\n");
        await RunChecksAsync();
        using var accepted = await CommitAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await BackdateAsync(upload.Id);

        Assert.Equal(1, await ClearExpiredFilesAsync());
        Assert.Equal(1, await RunCommitsAsync());

        using var failed = await GetAsync(fixture.Client, Tickets, upload.Id);
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        Assert.Equal([""], failed.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name));
        Assert.Equal(0L, await TicketCountAsync());
        Assert.Equal(0L, (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.process_work_items WHERE kind = '{RecordImports.CommitWorkItemKind}'"))!);
    }

    private static async Task<(HttpResponseMessage Response, JsonDocument Body, Guid Id)> UploadAsync(HttpClient client, string path, string csv, string? key = null)
    {
        var response = await SendAsync(client, HttpMethod.Post, $"{path}/imports{(key is null ? "" : $"?key={key}")}", csv);
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
        await fixture.ExecuteAsync(TenantA, $"DELETE FROM axis.process_work_items WHERE kind IN ('{RecordImports.CheckWorkItemKind}', '{RecordImports.CommitWorkItemKind}')");
    }

    private Task BackdateAsync(Guid id) =>
        fixture.ExecuteAsync(TenantA, $"UPDATE axis.record_imports SET created_at = now() - interval '25 hours' WHERE id = '{id:D}'");

    /// <summary>Runs the worker's clean-up of expired import files for the tenant and returns how many imports it changed.</summary>
    private async Task<int> ClearExpiredFilesAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        return await RecordImports.ClearExpiredFilesAsync(connection, DateTimeOffset.UtcNow, CancellationToken);
    }

    private async Task<string> StateAsync(Guid id, string path = Items)
    {
        using var import = await GetAsync(fixture.Client, path, id);
        return import.RootElement.GetProperty("state").GetString()!;
    }

    /// <summary>Claims and runs every dry run the tenant has queued, and returns how many ran.</summary>
    private async Task<int> RunChecksAsync(int maxRows = 10000)
    {
        var handler = new RecordImportCheckHandler(new ReleaseModelCache(PackageCatalog.Empty), new RecordImportSettings(maxRows), NullLoggerFactory.Instance, NullLogger<RecordImportCheckHandler>.Instance);
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

    /// <summary>Claims and runs every commit the tenant has queued, and returns how many ran.</summary>
    private async Task<int> RunCommitsAsync()
    {
        var handler = new RecordImportCommitHandler(new ReleaseModelCache(PackageCatalog.Empty), NullLoggerFactory.Instance, NullLogger<RecordImportCommitHandler>.Instance);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var ran = 0;
        while (await WorkItemQueue.ClaimAsync(connection, TenantA, [RecordImports.CommitWorkItemKind], TimeSpan.FromMinutes(5), CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await handler.ExecuteAsync(item, connection, CancellationToken));
            ran++;
        }

        return ran;
    }

    private static Task<HttpResponseMessage> CommitAsync(HttpClient client, string path, Guid id) =>
        SendAsync(client, HttpMethod.Post, $"{path}/imports/{id:D}/commit", null);

    private string TicketTable
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Ticket", out var ticket));
            return EntityNaming.QualifiedTable(EntityNaming.Table(ticket.Id));
        }
    }

    private async Task<Guid> CreateProductAsync(string name)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Products, $$"""{ "values": { "name": "{{name}}" } }""", "application/json");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private async Task CreateTicketAsync(string code, string title, int? priority)
    {
        var values = priority is { } number ? $$"""{ "title": "{{title}}", "code": "{{code}}", "priority": {{number}} }""" : $$"""{ "title": "{{title}}", "code": "{{code}}" }""";
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"{Tickets}/records", $$"""{ "values": {{values}} }""", "application/json");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<object?> TicketFieldAsync(string code, string column) =>
        await fixture.ScalarAsync(TenantA, $"SELECT {column} FROM {TicketTable} WHERE f_code = '{code}'");

    private async Task<Guid?> ProductOfAsync(string code) =>
        await fixture.ScalarAsync(TenantA, $"SELECT f_product FROM {TicketTable} WHERE f_code = '{code}'") as Guid?;

    private async Task<long> TicketCountAsync(string? where = null) =>
        (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {TicketTable}{(where is null ? "" : $" WHERE {where}")}"))!;

    private const string ImportedByAnnaFilter =
        "actor = 'requester' AND actor_name = 'Requester User' AND record_id IS NULL AND process_instance_id IS NULL AND entity_id IS NOT NULL AND details = '{\"created\": 3, \"updated\": 0}'::jsonb";

    private async Task<long> ImportedAuditCountAsync(string? where = null) =>
        (long)(await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM axis.audit_records WHERE action = 'records.imported'{(where is null ? "" : $" AND {where}")}"))!;

    private async Task<long> ItemCountAsync() =>
        (await fixture.TableShapeAsync(TenantA, "Item")).RowCount;

    private async Task<long> ImportCountAsync() =>
        (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.record_imports"))!;

    private async Task<bool> FileClearedAsync(Guid id) =>
        (bool)(await fixture.ScalarAsync(TenantA, $"SELECT file IS NULL FROM axis.record_imports WHERE id = '{id:D}'"))!;
}
