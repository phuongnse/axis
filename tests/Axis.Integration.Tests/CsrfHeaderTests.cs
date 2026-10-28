using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The CSRF header rule from D23: an unsafe request under <c>/api</c> without
/// <c>X-Axis-Csrf: 1</c> is a <c>403</c> before any work, with or without a session.
/// </summary>
public sealed class CsrfHeaderTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string Title = "The request must carry the X-Axis-Csrf header.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("1, 1")]
    public async Task Writes_without_the_header_are_a_403_and_change_nothing(string? header)
    {
        await AssertWritesRefusedAsync(fixture.Client, header);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("1, 1")]
    public async Task Writes_of_a_signed_in_user_without_the_header_are_a_403_and_change_nothing(string? header)
    {
        using var client = fixture.CreateClient().SignedInAs("requester");

        await AssertWritesRefusedAsync(client, header);

        using var me = Request("/api/me", HostA);
        using var meResponse = await client.SendAsync(me, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Writes_with_the_header_go_through()
    {
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });

        using var create = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair" } }""");
        using var created = await fixture.Client.SendAsync(create, CancellationToken);
        using var update = Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, """{ "version": 1, "values": { "name": "Table" } }""");
        using var updated = await fixture.Client.SendAsync(update, CancellationToken);
        using var delete = Request(HttpMethod.Delete, $"{Items}/{id:D}", HostA);
        using var deleted = await fixture.Client.SendAsync(delete, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>
    /// Sends a create, an update and a delete with <paramref name="header"/> in place of the CSRF
    /// header, or without it when null, and asserts each is refused and nothing is written.
    /// </summary>
    private async Task AssertWritesRefusedAsync(HttpClient client, string? header)
    {
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk", ["quantity"] = "2" });
        var (_, rowCount) = await fixture.TableShapeAsync(TenantA, "Item");
        var auditCount = await AuditCountAsync();

        using var create = WithHeader(Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Chair" } }"""), header);
        using var update = WithHeader(Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, """{ "version": 1, "values": { "name": "Table" } }"""), header);
        using var delete = WithHeader(Request(HttpMethod.Delete, $"{Items}/{id:D}", HostA), header);
        foreach (var request in new[] { create, update, delete })
        {
            using var response = await client.SendAsync(request, CancellationToken);
            using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal(Title, problem.RootElement.GetProperty("title").GetString());
        }

        using var get = Request($"{Items}/{id:D}", HostA);
        using var getResponse = await fixture.Client.SendAsync(get, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var record = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1, record.RootElement.GetProperty("version").GetInt64());
        Assert.Equal("Desk", record.RootElement.GetProperty("values").GetProperty("name").GetString());
        Assert.Equal(2, record.RootElement.GetProperty("values").GetProperty("quantity").GetInt32());
        Assert.Equal(rowCount, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Empty(await fixture.AuditRecordsAsync(TenantA, id));
        Assert.Equal(auditCount, await AuditCountAsync());
    }

    private static HttpRequestMessage WithHeader(HttpRequestMessage request, string? header)
    {
        request.Headers.Remove(CsrfHeader);
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(CsrfHeader, header);
        }

        return request;
    }

    private async Task<long> AuditCountAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(fixture.ConnectionString(TenantA));
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.audit_records");
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
