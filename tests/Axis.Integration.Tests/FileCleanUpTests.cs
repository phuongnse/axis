using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Marking replaced and removed files <c>deleted</c>, and keeping a file's name and id out of audit records, on the
/// FilesApp fixture in tenant A. <c>requester</c> may create and update claims but not delete them, so a delete is a call
/// of the command.
/// </summary>
public sealed class FileCleanUpTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";
    private const string Receipt = "/api/apps/FilesApp/entities/Claim/fields/receipt/files";
    private const string Attachment = "/api/apps/FilesApp/entities/Claim/fields/attachments/file/files";

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

    private async Task<string> StateAsync(Guid file) =>
        (string)(await fixture.ScalarAsync(TenantA, $"SELECT state FROM axis.files WHERE id = '{file:D}'"))!;

    private static async Task<string> StateAsync(NpgsqlConnection connection, Guid file)
    {
        await using var command = new NpgsqlCommand($"SELECT state FROM axis.files WHERE id = '{file:D}'", connection);
        return (string)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    /// <summary>A claim at version 2 with a receipt and two child rows, each with a file.</summary>
    private static async Task<(Guid Claim, Guid Receipt, Guid First, Guid Second)> ClaimWithFilesAsync(HttpClient client)
    {
        var claim = await CreateClaimAsync(client);
        var receipt = await UploadAsync(client, Receipt);
        var first = await UploadAsync(client, Attachment, "a note", "text/plain", "first.txt");
        var second = await UploadAsync(client, Attachment, "a note", "text/plain", "second.txt");
        using var response = await UpdateAsync(
            client,
            claim,
            1,
            $$"""{ "receipt": "{{receipt:D}}", "attachments": [{ "file": "{{first:D}}" }, { "file": "{{second:D}}" }] }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (claim, receipt, first, second);
    }

    private (EntityModel Claim, FieldModel Receipt, FieldModel Collection, EntityModel Child, FieldModel File) Model()
    {
        Assert.True(fixture.FilesModel.TryGetEntity("Claim", out var claim));
        var collection = claim.Fields.Single(field => field.Name == "attachments");
        var child = fixture.FilesModel.FindEntity(collection.Target!.Id)!;
        return (claim, claim.Fields.Single(field => field.Name == "receipt"), collection, child, child.Fields.Single(field => field.Name == "file"));
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    private async Task<RecordWriteResult> UpdateInTransactionAsync(
        NpgsqlConnection connection,
        Guid claim,
        long version,
        IReadOnlyList<RecordValue> values,
        IReadOnlyList<RecordRows> rows)
    {
        var model = Model();
        return await RecordCommands.UpdateAsync(
            connection,
            fixture.FilesModel,
            model.Claim,
            fixture.FilesReleaseId,
            claim,
            version,
            values,
            rows,
            uploader: "requester",
            cancellationToken: CancellationToken);
    }

    [Fact]
    public async Task An_update_that_replaces_the_receipt_marks_the_old_file_deleted_in_its_transaction()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, old, first, second) = await ClaimWithFilesAsync(requester);
        var replacement = await UploadAsync(requester, Receipt, fileName: "new.pdf");
        var model = Model();

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await UpdateInTransactionAsync(connection, claim, 2, [new RecordValue(model.Receipt, replacement)], []);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Equal("deleted", await StateAsync(connection, old));
        Assert.Equal("attached", await StateAsync(connection, replacement));
        Assert.Equal("attached", await StateAsync(connection, first));
        Assert.Equal("attached", await StateAsync(connection, second));
        await transaction.RollbackAsync(CancellationToken);
        Assert.Equal("attached", await StateAsync(old));
        Assert.Equal("pending", await StateAsync(replacement));
    }

    [Fact]
    public async Task An_update_that_clears_the_receipt_marks_the_old_file_deleted_in_its_transaction()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, old, first, _) = await ClaimWithFilesAsync(requester);
        var model = Model();

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await UpdateInTransactionAsync(connection, claim, 2, [new RecordValue(model.Receipt, null)], []);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Equal("deleted", await StateAsync(connection, old));
        Assert.Equal("attached", await StateAsync(connection, first));
        await transaction.RollbackAsync(CancellationToken);
    }

    [Fact]
    public async Task An_update_that_drops_a_child_row_marks_only_its_file_deleted()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, receipt, first, second) = await ClaimWithFilesAsync(requester);
        var model = Model();

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await UpdateInTransactionAsync(
            connection,
            claim,
            2,
            [],
            [new RecordRows(model.Collection, model.Child, [[new RecordValue(model.File, first)]])]);

        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        Assert.Equal("attached", await StateAsync(connection, first));
        Assert.Equal("deleted", await StateAsync(connection, second));
        Assert.Equal("attached", await StateAsync(connection, receipt));

        // An empty collection drops every child row, so it drops every file.
        var emptied = await UpdateInTransactionAsync(connection, claim, 3, [], [new RecordRows(model.Collection, model.Child, [])]);

        Assert.Equal(RecordWriteOutcome.Written, emptied.Outcome);
        Assert.Equal("deleted", await StateAsync(connection, first));
        Assert.Equal("attached", await StateAsync(connection, receipt));
        await transaction.RollbackAsync(CancellationToken);
    }

    [Fact]
    public async Task A_committed_update_through_the_api_leaves_the_replaced_file_deleted()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, old, first, second) = await ClaimWithFilesAsync(requester);
        var replacement = await UploadAsync(requester, Receipt, fileName: "new.pdf");

        using var response = await UpdateAsync(requester, claim, 2, $$"""{ "receipt": "{{replacement:D}}" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("deleted", await StateAsync(old));
        Assert.Equal("attached", await StateAsync(replacement));
        Assert.Equal("attached", await StateAsync(first));
        Assert.Equal("attached", await StateAsync(second));
    }

    [Fact]
    public async Task A_record_delete_marks_the_root_and_child_row_files_deleted_in_its_transaction()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, receipt, first, second) = await ClaimWithFilesAsync(requester);
        var other = await ClaimWithFilesAsync(requester);
        var model = Model();

        await using var connection = await OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.DeleteAsync(connection, fixture.FilesModel, model.Claim, claim, CancellationToken);

        Assert.Equal(RecordDeleteOutcome.Deleted, result.Outcome);
        foreach (var file in new[] { receipt, first, second })
        {
            Assert.Equal("deleted", await StateAsync(connection, file));
        }

        Assert.Equal("attached", await StateAsync(connection, other.Receipt));
        await transaction.RollbackAsync(CancellationToken);
        foreach (var file in new[] { receipt, first, second })
        {
            Assert.Equal("attached", await StateAsync(file));
        }
    }

    [Fact]
    public async Task An_update_refused_for_a_stale_version_leaves_the_old_file_attached_and_the_new_upload_pending()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, old, _, _) = await ClaimWithFilesAsync(requester);
        var replacement = await UploadAsync(requester, Receipt, fileName: "new.pdf");

        using var response = await UpdateAsync(requester, claim, 1, $$"""{ "receipt": "{{replacement:D}}" }""");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("attached", await StateAsync(old));
        Assert.Equal("pending", await StateAsync(replacement));
    }

    [Fact]
    public async Task A_file_marked_deleted_cannot_be_sent_again()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var (claim, old, _, _) = await ClaimWithFilesAsync(requester);
        var replacement = await UploadAsync(requester, Receipt, fileName: "new.pdf");
        using (var replaced = await UpdateAsync(requester, claim, 2, $$"""{ "receipt": "{{replacement:D}}" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        }

        using var response = await UpdateAsync(requester, claim, 3, $$"""{ "receipt": "{{old:D}}" }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("/values/receipt", Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject()).Name);
        Assert.Equal("deleted", await StateAsync(old));
    }

    [Fact]
    public async Task The_audit_record_of_a_receipt_change_names_the_field_and_neither_the_file_id_nor_its_name()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester);
        var receipt = await UploadAsync(requester, Receipt, fileName: "scan-417.pdf");

        using var response = await UpdateAsync(requester, claim, 1, $$"""{ "receipt": "{{receipt:D}}" }""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = Assert.Single(await fixture.AuditRecordsAsync(TenantA, claim), record => record.Action == "record.updated");
        using var details = JsonDocument.Parse(updated.Details);
        Assert.Equal(["receipt"], details.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        Assert.DoesNotContain(receipt.ToString("D"), updated.Details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scan-417", updated.Details, StringComparison.OrdinalIgnoreCase);
    }
}
