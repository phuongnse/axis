using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Files;
using Axis.Data.Records;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Attaching an uploaded file by a record write, on the FilesApp fixture in tenant A. <c>anna</c> may create and
/// update claims. A claim has a <c>receipt</c> file field and an <c>attachments</c> child collection whose rows
/// have a <c>file</c> field.
/// </summary>
public sealed class FileAttachTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";
    private const string Receipt = "/api/apps/FilesApp/entities/Claim/fields/receipt/files";
    private const string Attachment = "/api/apps/FilesApp/entities/Claim/fields/attachments/file/files";
    private const string UnknownFile = "No uploaded file for this field has this id.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static HttpRequestMessage Upload(string path, string content, string mediaType, string fileName)
    {
        var request = Request(HttpMethod.Post, path, HostA);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        part.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        form.Add(part, "file", fileName);
        request.Content = form;
        return request;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, string path, string content = "%PDF-1.7\n", string mediaType = "application/pdf", string fileName = "receipt.pdf")
    {
        using var response = await client.SendAsync(Upload(path, content, mediaType, fileName), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateClaimAsync(HttpClient client)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, Claims, HostA, """{ "values": { "title": "Lost" } }"""), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UpdateAsync(HttpClient client, Guid claim, long version, string values) =>
        await client.SendAsync(Request(HttpMethod.Patch, $"{Claims}/{claim:D}", HostA, $$"""{ "version": {{version}}, "values": {{values}} }"""), CancellationToken);

    private static async Task<JsonDocument> ReadAsync(HttpClient client, Guid claim)
    {
        using var response = await client.SendAsync(Request($"{Claims}/{claim:D}", HostA), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<string> StoredAsync(Guid file, string column) =>
        (string)(await fixture.ScalarAsync(TenantA, $"SELECT {column}::text FROM axis.files WHERE id = '{file:D}'"))!;

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string pointer)
    {
        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal(pointer, error.Name);
        Assert.Equal(UnknownFile, Assert.Single(error.Value.EnumerateArray()).GetString());
    }

    [Fact]
    public async Task An_update_attaches_a_receipt_and_a_child_row_file_and_a_read_returns_their_details()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);
        var receipt = await UploadAsync(anna, Receipt);
        var note = await UploadAsync(anna, Attachment, "a note", "text/plain", "note.txt");

        using var updated = await UpdateAsync(anna, claim, 1, $$"""{ "receipt": "{{receipt:D}}", "attachments": [{ "file": "{{note:D}}", "note": "Scan" }] }""");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var written = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(receipt, written.RootElement.GetProperty("values").GetProperty("receipt").GetProperty("id").GetGuid());
        using var read = await ReadAsync(anna, claim);
        var values = read.RootElement.GetProperty("values");
        Assert.Equal($$"""{"id":"{{receipt:D}}","name":"receipt.pdf","mediaType":"application/pdf","size":9}""", values.GetProperty("receipt").GetRawText());
        Assert.Equal(
            $$"""{"id":"{{note:D}}","name":"note.txt","mediaType":"text/plain","size":6}""",
            values.GetProperty("attachments")[0].GetProperty("file").GetRawText());
        Assert.Equal("Scan", values.GetProperty("attachments")[0].GetProperty("note").GetString());
        foreach (var file in new[] { receipt, note })
        {
            Assert.Equal("attached", await StoredAsync(file, "state"));
            Assert.Equal(claim.ToString("D"), await StoredAsync(file, "owner_record_id"));
        }

        // A list read returns the same object.
        using var list = await anna.SendAsync(Request($"{Claims}?pageSize=100", HostA), CancellationToken);
        using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
        var item = page.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == claim);
        Assert.Equal(receipt, item.GetProperty("values").GetProperty("receipt").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_create_attaches_a_file_to_the_new_record()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var receipt = await UploadAsync(anna, Receipt);

        using var response = await anna.SendAsync(
            Request(HttpMethod.Post, Claims, HostA, $$"""{ "values": { "title": "New", "receipt": "{{receipt:D}}" } }"""),
            CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var claim = body.RootElement.GetProperty("id").GetString();
        Assert.Equal("receipt.pdf", body.RootElement.GetProperty("values").GetProperty("receipt").GetProperty("name").GetString());
        Assert.Equal("attached", await StoredAsync(receipt, "state"));
        Assert.Equal(claim, await StoredAsync(receipt, "owner_record_id"));
    }

    [Fact]
    public async Task A_null_receipt_reads_as_null()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);

        using var read = await ReadAsync(anna, claim);

        Assert.Equal(JsonValueKind.Null, read.RootElement.GetProperty("values").GetProperty("receipt").ValueKind);
    }

    [Fact]
    public async Task A_file_id_that_does_not_qualify_gets_400_with_one_message_at_the_field()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);

        var foreign = Guid.CreateVersion7();
        Assert.True(fixture.FilesModel.TryGetEntity("Claim", out var entity));
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA)))
        {
            await connection.OpenAsync(CancellationToken);
            await FileStore.InsertPendingAsync(
                connection,
                new PendingFile(foreign, fixture.FilesModel.Manifest.Id, entity.Id, "receipt", "other.pdf", "application/pdf", 3, new byte[32], "binh", DateTimeOffset.UtcNow),
                new byte[] { 1, 2, 3 },
                CancellationToken);
        }

        var otherClaim = await CreateClaimAsync(anna);
        var elsewhere = await UploadAsync(anna, Receipt);
        using (var attached = await UpdateAsync(anna, otherClaim, 1, $$"""{ "receipt": "{{elsewhere:D}}" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        }

        var otherField = await UploadAsync(anna, Attachment, "a note", "text/plain", "note.txt");

        foreach (var id in new[] { foreign, elsewhere, otherField, Guid.NewGuid() })
        {
            using var response = await UpdateAsync(anna, claim, 1, $$"""{ "receipt": "{{id:D}}" }""");
            await AssertRefusedAsync(response, "/values/receipt");
        }

        // Nothing was attached to the claim, and the foreign file is still pending.
        Assert.Equal("pending", await StoredAsync(foreign, "state"));
        Assert.Equal("pending", await StoredAsync(otherField, "state"));
        Assert.Equal(otherClaim.ToString("D"), await StoredAsync(elsewhere, "owner_record_id"));
        using var read = await ReadAsync(anna, claim);
        Assert.Equal(1, read.RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task A_child_row_file_that_does_not_qualify_gets_400_at_the_row_and_a_repeated_id_at_the_later_cell()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);
        var receipt = await UploadAsync(anna, Receipt);
        var note = await UploadAsync(anna, Attachment, "a note", "text/plain", "note.txt");

        using var wrongField = await UpdateAsync(anna, claim, 1, $$"""{ "attachments": [{ "file": "{{receipt:D}}" }] }""");
        using var repeated = await UpdateAsync(anna, claim, 1, $$"""{ "attachments": [{ "file": "{{note:D}}" }, { "file": "{{note:D}}" }] }""");

        await AssertRefusedAsync(wrongField, "/values/attachments/0/file");
        await AssertRefusedAsync(repeated, "/values/attachments/1/file");
        Assert.Equal("pending", await StoredAsync(note, "state"));
    }

    [Fact]
    public async Task Resending_the_same_file_ids_keeps_the_files()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);
        var receipt = await UploadAsync(anna, Receipt);
        var note = await UploadAsync(anna, Attachment, "a note", "text/plain", "note.txt");
        var values = $$"""{ "receipt": "{{receipt:D}}", "attachments": [{ "file": "{{note:D}}", "note": "Scan" }] }""";
        using (var first = await UpdateAsync(anna, claim, 1, values))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var again = await UpdateAsync(anna, claim, 2, values);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var read = await ReadAsync(anna, claim);
        Assert.Equal(receipt, read.RootElement.GetProperty("values").GetProperty("receipt").GetProperty("id").GetGuid());
        Assert.Equal(note, read.RootElement.GetProperty("values").GetProperty("attachments")[0].GetProperty("file").GetProperty("id").GetGuid());
        Assert.Equal("attached", await StoredAsync(receipt, "state"));
        Assert.Equal("attached", await StoredAsync(note, "state"));
    }

    [Fact]
    public async Task A_write_with_no_uploader_attaches_nothing_and_is_invalid()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var claim = await CreateClaimAsync(anna);
        var receipt = await UploadAsync(anna, Receipt);
        Assert.True(fixture.FilesModel.TryGetEntity("Claim", out var entity));
        var field = entity.Fields.Single(candidate => candidate.Name == "receipt");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.UpdateAsync(
            connection,
            fixture.FilesModel,
            entity,
            fixture.FilesReleaseId,
            claim,
            1,
            [new RecordValue(field, receipt)],
            [],
            cancellationToken: CancellationToken);
        await transaction.RollbackAsync(CancellationToken);

        Assert.Equal(RecordWriteOutcome.Invalid, result.Outcome);
        var error = Assert.Single(result.Errors!);
        Assert.Equal("/values/receipt", error.Key);
        Assert.Equal([UnknownFile], error.Value);
        Assert.Equal("pending", await StoredAsync(receipt, "state"));
    }
}
