using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The record API of tenant A served through <see cref="PgBouncerServer"/> in transaction mode, as a cell serves it. The server's tenant
/// connection is the runtime role through PgBouncer. Everything else, such as the schema connection, stays direct.
/// </summary>
public sealed class PgBouncerRecordApiTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_record_is_created_updated_and_listed_through_PgBouncer_with_the_same_results_as_directly()
    {
        await fixture.ResetAsync();
        var runtime = await PgBouncerServer.RuntimeConnectionStringAsync(fixture.ConnectionString(TenantA));
        await using var pooled = fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Tenants:a:ConnectionString", runtime));
        using var client = pooled.CreateClient().SignedInAs("requester");

        using var create = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk", "quantity": 3, "price": 10 } }""");
        using var created = await client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken)).RootElement.GetProperty("id").GetGuid();
        await AssertSameReadsAsync(client, id, "Desk");

        using var update = Request(HttpMethod.Patch, $"{Items}/{id:D}", HostA, """{ "version": 1, "values": { "name": "Desk 2", "quantity": 5 } }""");
        using var updated = await client.SendAsync(update, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var list = await AssertSameReadsAsync(client, id, "Desk 2");

        Assert.Contains("Desk 2", list);
        await PgBouncerServer.AssertNoRuntimeTransactionLeftOpenAsync(fixture.ConnectionString(TenantA), onlyThroughPgBouncer: false);
    }

    [Fact]
    public async Task The_schema_role_cannot_connect_through_PgBouncer()
    {
        var schema = new NpgsqlConnectionStringBuilder(await PgBouncerServer.ThroughAsync(fixture.ConnectionString(TenantA)))
        {
            Pooling = false,
            Timeout = 15,
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(schema);

        await Assert.ThrowsAsync<PostgresException>(() => connection.OpenAsync(CancellationToken));
    }

    // Reads the record and the list through PgBouncer and directly, asserts that each pair is equal and returns the list body.
    private async Task<string> AssertSameReadsAsync(HttpClient pooled, Guid id, string name)
    {
        var path = $"{Items}/{id:D}";
        var direct = await ReadAsync(fixture.Client, path);
        var throughPgBouncer = await ReadAsync(pooled, path);
        Assert.Equal(direct, throughPgBouncer);
        Assert.Equal(name, JsonDocument.Parse(throughPgBouncer).RootElement.GetProperty("values").GetProperty("name").GetString());

        var directList = await ReadAsync(fixture.Client, $"{Items}?pageSize=100");
        var pooledList = await ReadAsync(pooled, $"{Items}?pageSize=100");
        Assert.Equal(directList, pooledList);
        return pooledList;
    }

    private static async Task<string> ReadAsync(HttpClient client, string path)
    {
        using var request = Request(path, HostA);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(CancellationToken);
    }
}
