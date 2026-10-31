using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The record API stores a sensitive value as ciphertext and reads it back, also in the rows of a child collection. It runs on the
/// FilesApp fixture in tenant A, where the <c>note</c> field of a claim and the <c>secret</c> field of its attachment rows are
/// <c>sensitive</c>. The host of the fixture holds no data key for tenant A, so a test that stores a value derives a host that holds one.
/// </summary>
public sealed class RecordSensitiveValueTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Keyed() =>
        fixture.Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Tenants:a:DataKeys:1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    private string ClaimTable() => TableOf("Claim");

    private string AttachmentTable() => TableOf("ClaimAttachment");

    private string TableOf(string entityName)
    {
        Assert.True(fixture.FilesModel.TryGetEntity(entityName, out var entity));
        return EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string body)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, Claims, HostA, body), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return created.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Guid claim) =>
        await client.SendAsync(Request($"{Claims}/{claim:D}", HostA), CancellationToken);

    /// <summary>Reads a column as the runtime role: its type name and its bytes.</summary>
    private async Task<(string Type, byte[] Bytes)> StoredAsync(string table, string column, string where)
    {
        await using var connection = new NpgsqlConnection(PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString(TenantA)));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"SELECT {EntityNaming.Quote(column)} FROM {table} WHERE {where}", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetDataTypeName(0), reader.GetFieldValue<byte[]>(0));
    }

    private async Task DeleteAsync(params Guid[] claims)
    {
        foreach (var claim in claims)
        {
            await fixture.ExecuteAsync(TenantA, $"DELETE FROM {AttachmentTable()} WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = '{claim:D}'");
            await fixture.ExecuteAsync(TenantA, $"DELETE FROM {ClaimTable()} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = '{claim:D}'");
        }
    }

    [Fact]
    public async Task A_create_reads_back_through_get_and_list_and_is_stored_as_bytes_without_the_value()
    {
        await using var keyed = Keyed();
        using var requester = keyed.CreateClient().SignedInAs("requester");
        const string note = "Handwritten note of Ngô Thị Hạnh";

        var claim = await CreateAsync(requester, $$"""{ "values": { "title": "Sealed", "note": "{{note}}" } }""");

        using var get = await GetAsync(requester, claim);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var read = JsonDocument.Parse(await get.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(note, read.RootElement.GetProperty("values").GetProperty("note").GetString());

        using var list = await requester.SendAsync(Request($"{Claims}?pageSize=100", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
        var item = page.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == claim);
        Assert.Equal(note, item.GetProperty("values").GetProperty("note").GetString());

        var (type, bytes) = await StoredAsync(ClaimTable(), EntityNaming.Column("note"), $"{EntityNaming.Quote(EntityNaming.IdColumn)} = '{claim:D}'");
        Assert.Equal("bytea", type);
        Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(note)) >= 0);

        // An update seals the new value too.
        using var updated = await requester.SendAsync(
            Request(HttpMethod.Patch, $"{Claims}/{claim:D}", HostA, """{ "version": 1, "values": { "note": "Second note" } }"""), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var again = JsonDocument.Parse(await (await GetAsync(requester, claim)).Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("Second note", again.RootElement.GetProperty("values").GetProperty("note").GetString());
    }

    [Fact]
    public async Task A_stored_value_copied_to_another_record_or_a_child_row_does_not_read()
    {
        await using var keyed = Keyed();
        using var requester = keyed.CreateClient().SignedInAs("requester");
        var source = await CreateAsync(requester, """{ "values": { "title": "Copy source", "note": "Source note" } }""");
        var otherRecord = await CreateAsync(requester, """{ "values": { "title": "Copy target", "note": "Other note" } }""");
        var withRow = await CreateAsync(requester, """{ "values": { "title": "Copy row", "attachments": [{ "note": "Row", "secret": "Row secret" }] } }""");
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        var note = EntityNaming.Quote(EntityNaming.Column("note"));
        var secret = EntityNaming.Quote(EntityNaming.Column("secret"));
        try
        {
            await fixture.ExecuteAsync(
                TenantA,
                $"UPDATE {ClaimTable()} SET {note} = (SELECT {note} FROM {ClaimTable()} WHERE {idColumn} = '{source:D}') WHERE {idColumn} = '{otherRecord:D}'");
            using var movedRecord = await GetAsync(requester, otherRecord);
            Assert.Equal(HttpStatusCode.InternalServerError, movedRecord.StatusCode);

            await fixture.ExecuteAsync(
                TenantA,
                $"UPDATE {AttachmentTable()} SET {secret} = (SELECT {note} FROM {ClaimTable()} WHERE {idColumn} = '{source:D}') WHERE {EntityNaming.Quote(EntityNaming.OwnerColumn)} = '{withRow:D}'");
            using var movedRow = await GetAsync(requester, withRow);
            Assert.Equal(HttpStatusCode.InternalServerError, movedRow.StatusCode);

            // The source is untouched.
            using var sourceRead = await GetAsync(requester, source);
            Assert.Equal(HttpStatusCode.OK, sourceRead.StatusCode);
        }
        finally
        {
            await DeleteAsync(source, otherRecord, withRow);
        }
    }

    [Fact]
    public async Task A_sensitive_field_of_a_child_row_is_stored_as_bytes_and_reads_back_in_its_row()
    {
        await using var keyed = Keyed();
        using var requester = keyed.CreateClient().SignedInAs("requester");
        const string secret = "Row secret of Lê Văn Minh";

        var claim = await CreateAsync(
            requester,
            $$"""{ "values": { "title": "With rows", "attachments": [{ "note": "First" }, { "note": "Second", "secret": "{{secret}}" }] } }""");

        var (type, bytes) = await StoredAsync(
            AttachmentTable(),
            EntityNaming.Column("secret"),
            $"{EntityNaming.Quote(EntityNaming.OwnerColumn)} = '{claim:D}' AND {EntityNaming.Quote(EntityNaming.Column("secret"))} IS NOT NULL");
        Assert.Equal("bytea", type);
        Assert.False(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) >= 0);

        using var get = await GetAsync(requester, claim);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var read = JsonDocument.Parse(await get.Content.ReadAsStringAsync(CancellationToken));
        var rows = read.RootElement.GetProperty("values").GetProperty("attachments");
        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("secret").ValueKind);
        Assert.Equal("Second", rows[1].GetProperty("note").GetString());
        Assert.Equal(secret, rows[1].GetProperty("secret").GetString());
    }

    [Fact]
    public async Task A_create_with_a_sensitive_value_and_no_data_key_is_a_500_that_logs_the_tenant_and_stores_no_row()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        const string title = "No key claim";
        var errors = fixture.Logs.Records.Count(record => record.Level == LogLevel.Error && record.Message.Contains("Tenant a has no data key", StringComparison.Ordinal));

        using var response = await requester.SendAsync(
            Request(HttpMethod.Post, Claims, HostA, $$"""{ "values": { "title": "{{title}}", "note": "Never stored" } }"""), CancellationToken);

        using var problem = await ReadProblemAsync(response, HttpStatusCode.InternalServerError);
        AssertNoLeak(problem);
        Assert.Equal(
            errors + 1,
            fixture.Logs.Records.Count(record => record.Level == LogLevel.Error && record.Message.Contains("Tenant a has no data key", StringComparison.Ordinal)));
        Assert.Equal(
            0L,
            await fixture.ScalarAsync(TenantA, $"SELECT count(*) FROM {ClaimTable()} WHERE {EntityNaming.Quote(EntityNaming.Column("title"))} = '{title}'"));
    }
}
