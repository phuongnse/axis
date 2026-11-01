using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Packages;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>claims</c> module holds a grant ceiling on its <c>Claim</c> entity. The <c>filer</c> slot may read,
/// create, import and export every claim, and update only a claim whose <c>requester</c> is the user. The
/// <c>viewer</c> slot may read and export only a claim whose <c>requester</c> is the user. ModuleCeilingApp maps
/// <c>filer</c> to <c>clerk</c> and <c>viewer</c> to <c>auditor</c> and grants both roles every record, so the
/// ceiling's filters alone narrow the record API, the data source endpoint, export and import.
/// </summary>
public sealed class ModuleCeilingTests(ModuleAppFixture fixture) : IClassFixture<ModuleAppFixture>
{
    private const string Clerk = "ceiling-clerk";
    private const string Auditor = "ceiling-auditor";
    private const string Records = "/api/apps/ModuleCeilingApp/entities/claims/Claim/records";
    private const string Imports = "/api/apps/ModuleCeilingApp/entities/claims/Claim/imports";
    private const string ClaimsDataSource = "/api/apps/ModuleCeilingApp/data-sources/claims/Claims";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_updates_its_own_record_but_not_another_users_through_an_unfiltered_grant_and_a_filtered_ceiling_entry()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var own = await CreateAsync(clerk, Clerk, "Own");
        var other = await CreateAsync(clerk, "somebody-else", "Other");

        using (var updated = await SendAsync(clerk, HttpMethod.Patch, $"{Records}/{own.Id}", $$"""{ "version": {{own.Version}}, "values": { "title": "Own changed" } }"""))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        using (var refused = await SendAsync(clerk, HttpMethod.Patch, $"{Records}/{other.Id}", $$"""{ "version": {{other.Version}}, "values": { "title": "Other changed" } }"""))
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using var unchanged = await GetJsonAsync(clerk, $"{Records}/{other.Id}");
        Assert.Equal("Other", unchanged.RootElement.GetProperty("values").GetProperty("title").GetString());
        Assert.Equal(other.Version, unchanged.RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task A_role_reads_and_lists_every_record_through_a_ceiling_entry_without_a_filter()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var own = await CreateAsync(clerk, Clerk, "Own");
        var other = await CreateAsync(clerk, "somebody-else", "Other");

        using var read = await SendAsync(clerk, HttpMethod.Get, $"{Records}/{other.Id}");
        using var list = await GetJsonAsync(clerk, $"{Records}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var listed = list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(own.Id, listed);
        Assert.Contains(other.Id, listed);
    }

    [Fact]
    public async Task A_role_with_a_filtered_read_entry_reaches_only_its_own_records_through_the_record_api_and_the_data_source()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var own = await CreateAsync(clerk, Auditor, "Audited");
        var other = await CreateAsync(clerk, "somebody-else", "Other");
        using var auditor = fixture.CreateClient().SignedInAs(Auditor);

        using var readOwn = await SendAsync(auditor, HttpMethod.Get, $"{Records}/{own.Id}");
        using var readOther = await SendAsync(auditor, HttpMethod.Get, $"{Records}/{other.Id}");
        using var list = await GetJsonAsync(auditor, $"{Records}?pageSize=100");
        using var rows = await GetJsonAsync(auditor, $"{ClaimsDataSource}/rows?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, readOwn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, readOther.StatusCode);
        var listed = list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(own.Id, listed);
        Assert.DoesNotContain(other.Id, listed);
        var requesters = rows.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("values").GetProperty("requester").GetString())
            .ToList();
        Assert.Contains(requesters, requester => requester == Auditor);
        Assert.All(requesters, requester => Assert.Equal(Auditor, requester));
    }

    [Fact]
    public async Task An_export_of_a_role_with_a_filtered_read_entry_holds_only_its_own_records()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var own = await CreateAsync(clerk, Auditor, "Audited");
        var other = await CreateAsync(clerk, "somebody-else", "Other");
        using var auditor = fixture.CreateClient().SignedInAs(Auditor);

        using var response = await SendAsync(auditor, HttpMethod.Get, $"{Records}/export?format=csv");
        using var rowsResponse = await SendAsync(auditor, HttpMethod.Get, $"{ClaimsDataSource}/export?format=csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rowsResponse.StatusCode);
        foreach (var file in new[] { response, rowsResponse })
        {
            var rows = await ReadCsvAsync(file);
            Assert.Equal(["title", "code", "requester"], rows[0]);
            Assert.Contains(rows.Skip(1), row => row[0] == "Audited");
            Assert.DoesNotContain(rows.Skip(1), row => row[0] == "Other");
            Assert.All(rows.Skip(1), row => Assert.Equal(Auditor, row[2]));
        }

        Assert.NotEqual(own.Id, other.Id);
    }

    [Fact]
    public async Task A_keyed_import_cannot_update_a_record_past_the_ceilings_update_filter()
    {
        using var clerk = fixture.CreateClient().SignedInAs(Clerk);
        var own = await CreateAsync(clerk, Clerk, "Own");
        var other = await CreateAsync(clerk, "somebody-else", "Other");

        using var upload = await SendAsync(
            clerk, HttpMethod.Post, $"{Imports}?key=code", $"code,title\n{other.Code},Hijacked\n{own.Code},Renamed\n", "text/csv");
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        Guid importId;
        using (var accepted = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(CancellationToken)))
        {
            importId = accepted.RootElement.GetProperty("id").GetGuid();
        }

        Assert.Equal(1, await RunChecksAsync());

        using var failed = await GetJsonAsync(clerk, $"{Imports}/{importId:D}");
        Assert.Equal("failed", failed.RootElement.GetProperty("state").GetString());
        var errors = failed.RootElement.GetProperty("errors");
        Assert.Equal(["/rows/1"], errors.EnumerateObject().Select(error => error.Name));
        Assert.Equal("The record is outside the uploader's update filters.", errors.GetProperty("/rows/1")[0].GetString());
        using var unchanged = await GetJsonAsync(clerk, $"{Records}/{other.Id}");
        Assert.Equal("Other", unchanged.RootElement.GetProperty("values").GetProperty("title").GetString());
        Assert.Equal(other.Version, unchanged.RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task A_role_whose_ceiling_has_no_create_entry_for_it_is_denied_a_create()
    {
        using var auditor = fixture.CreateClient().SignedInAs(Auditor);

        using var response = await SendAsync(auditor, HttpMethod.Post, Records, """{ "values": { "title": "Denied", "requester": "ceiling-auditor" } }""");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null, string? contentType = "application/json")
    {
        using var request = RecordApiFixture.Request(method, path, ModuleAppFixture.Host, body, contentType);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await SendAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static async Task<(Guid Id, long Version, string Code)> CreateAsync(HttpClient client, string requester, string title)
    {
        var code = $"c{Guid.NewGuid():N}";
        using var response = await SendAsync(
            client, HttpMethod.Post, Records, $$"""{ "values": { "title": "{{title}}", "code": "{{code}}", "requester": "{{requester}}" } }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64(), code);
    }

    private static async Task<List<List<string>>> ReadCsvAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(CancellationToken);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        return [.. CsvText.Parse(Encoding.UTF8.GetString(bytes[3..])).Select(row => row.ToList())];
    }

    /// <summary>Claims and runs every dry run the tenant has queued, and returns how many ran.</summary>
    private async Task<int> RunChecksAsync()
    {
        var handler = new RecordImportCheckHandler(
            new ReleaseModelCache(PackageCatalog.Empty),
            new RecordImportSettings(10000),
            new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(new ConfigurationBuilder().Build()))),
            NullLoggerFactory.Instance,
            NullLogger<RecordImportCheckHandler>.Instance);
        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        var ran = 0;
        while (await WorkItemQueue.ClaimAsync(connection, ModuleAppFixture.Tenant, [RecordImports.CheckWorkItemKind], TimeSpan.FromMinutes(5), CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await handler.ExecuteAsync(item, connection, CancellationToken));
            ran++;
        }

        return ran;
    }
}
