using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The access the pages of the built-in <c>approvals</c> module rely on, on ApprovalsApp, which maps the slot
/// <c>admin</c> to <c>admin</c> and the slot <c>member</c> to <c>employee</c>. A member is refused the <c>Requests</c> and
/// <c>RequestTypes</c> data sources, so All requests and Request types stay with administrators, and every write of a
/// request type. The <c>MyRequests</c> data source holds only the requests the member submitted. The application has a
/// member policy of its own that reads every request, so this checks the data source's filter and not the module's
/// read filter.
/// </summary>
public sealed class ApprovalsPageAccessTests(ApprovalsAppFixture fixture) : IClassFixture<ApprovalsAppFixture>
{
    private const string Admin = "appr-admin";
    private const string RequestTypes = "/api/apps/ApprovalsApp/entities/approvals/RequestType/records";
    private const string Requests = "/api/apps/ApprovalsApp/entities/approvals/Request/records";
    private const string DataSources = "/api/apps/ApprovalsApp/data-sources/approvals";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_is_refused_the_request_data_sources_and_every_write_of_a_request_type_and_an_administrator_reads_the_requests()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var type = await CreateAsync(admin, RequestTypes, RequestType());
        using var member = fixture.CreateClient().SignedInAs("appr-carl");

        using var requestRows = await SendAsync(member, HttpMethod.Get, $"{DataSources}/Requests/rows");
        using var typeRows = await SendAsync(member, HttpMethod.Get, $"{DataSources}/RequestTypes/rows");
        using var createType = await SendAsync(member, HttpMethod.Post, RequestTypes, $$"""{ "values": {{RequestType()}} }""");
        using var updateType = await SendAsync(
            member, HttpMethod.Patch, $"{RequestTypes}/{type.Id}", $$"""{ "version": {{type.Version}}, "values": { "name": "Renamed" } }""");
        using var deleteType = await SendAsync(member, HttpMethod.Delete, $"{RequestTypes}/{type.Id}");

        foreach (var response in new[] { requestRows, typeRows, createType, updateType, deleteType })
        {
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        }

        using var adminRows = await SendAsync(admin, HttpMethod.Get, $"{DataSources}/Requests/rows");
        Assert.Equal(HttpStatusCode.OK, adminRows.StatusCode);
    }

    [Fact]
    public async Task The_my_requests_rows_of_a_member_hold_only_the_requests_they_submitted()
    {
        using var admin = fixture.CreateClient().SignedInAs(Admin);
        var type = await CreateAsync(admin, RequestTypes, RequestType());
        using var ann = fixture.CreateClient().SignedInAs("appr-ann");
        using var bob = fixture.CreateClient().SignedInAs("appr-bob");
        var first = await CreateAsync(ann, Requests, $$"""{ "type": "{{type.Id}}", "title": "Ann one" }""");
        var second = await CreateAsync(ann, Requests, $$"""{ "type": "{{type.Id}}", "title": "Ann two" }""");
        await CreateAsync(bob, Requests, $$"""{ "type": "{{type.Id}}", "title": "Bob one" }""");

        using var response = await SendAsync(ann, HttpMethod.Get, $"{DataSources}/MyRequests/rows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var ids = rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order().ToList();
        Assert.Equal(new[] { first.Id, second.Id }.Order(), ids);
        Assert.Equal(2, rows.RootElement.GetProperty("totalCount").GetInt64());
    }

    private static string RequestType() =>
        $$"""{ "name": "Type", "code": "{{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}}", "steps": [{ "sequence": 1, "approver": "lineManager" }] }""";

    private static async Task<(Guid Id, long Version)> CreateAsync(HttpClient client, string records, string values)
    {
        using var response = await SendAsync(client, HttpMethod.Post, records, $$"""{ "values": {{values}} }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("id").GetGuid(), body.RootElement.GetProperty("version").GetInt64());
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body = null) =>
        client.SendAsync(Request(method, path, ApprovalsAppFixture.Host, body), CancellationToken);
}
