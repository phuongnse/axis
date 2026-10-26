using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The upload route of a file field, on the FilesApp fixture in tenant A. Its <c>Claim</c> entity has a
/// <c>receipt</c> file field with a <c>maxSize</c> of 1024 bytes that accepts PDF and any image, and an
/// <c>attachments</c> child collection whose rows have a <c>file</c> field. <c>anna</c> may create and update
/// claims, <c>rita</c> may only read them, and <c>fia</c> may update only their <c>title</c>.
/// </summary>
public sealed class FileUploadTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Receipt = "/api/apps/FilesApp/entities/Claim/fields/receipt/files";
    private const string Attachment = "/api/apps/FilesApp/entities/Claim/fields/attachments/file/files";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static byte[] Pdf(int length)
    {
        var bytes = new byte[length];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    private static HttpRequestMessage Upload(string path, byte[] bytes, string? mediaType, string? fileName = "invoice.pdf", string partName = "file")
    {
        var request = Request(HttpMethod.Post, path, HostA);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        if (mediaType is not null)
        {
            part.Headers.TryAddWithoutValidation("Content-Type", mediaType);
        }

        if (fileName is null)
        {
            form.Add(part, partName);
        }
        else
        {
            form.Add(part, partName, fileName);
        }

        request.Content = form;
        return request;
    }

    private async Task<long> FileCountAsync() => (long)(await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.files"))!;

    private async Task<string> StoredAsync(Guid id, string column) =>
        (string)(await fixture.ScalarAsync(TenantA, $"SELECT {column}::text FROM axis.files WHERE id = '{id:D}'"))!;

    [Fact]
    public async Task A_user_with_an_update_grant_uploads_a_file_that_is_stored_as_pending()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var bytes = Pdf(300);

        using var response = await anna.SendAsync(Upload(Receipt, bytes, "application/pdf", "C:\\scans\\invoice.pdf"), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["id", "name", "mediaType", "size"], body.RootElement.EnumerateObject().Select(property => property.Name));
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("invoice.pdf", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("application/pdf", body.RootElement.GetProperty("mediaType").GetString());
        Assert.Equal(300, body.RootElement.GetProperty("size").GetInt64());

        Assert.Equal("pending", await StoredAsync(id, "state"));
        Assert.Equal("anna", await StoredAsync(id, "uploaded_by"));
        Assert.Equal("receipt", await StoredAsync(id, "field_path"));
        Assert.Equal("database", await StoredAsync(id, "storage_kind"));
        Assert.Equal("invoice.pdf", await StoredAsync(id, "name"));
        Assert.Equal("application/pdf", await StoredAsync(id, "media_type"));
        Assert.Equal(300, long.Parse(await StoredAsync(id, "size"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(await fixture.ScalarAsync(TenantA, $"SELECT owner_record_id FROM axis.files WHERE id = '{id:D}'"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), await StoredAsync(id, "encode(sha256, 'hex')"));
        Assert.Equal(
            Convert.ToHexStringLower(bytes),
            (string)(await fixture.ScalarAsync(TenantA, $"SELECT encode(content, 'hex') FROM axis.file_contents WHERE file_id = '{id:D}'"))!);
    }

    [Fact]
    public async Task A_field_of_a_child_entity_is_stored_under_its_path()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var bytes = Encoding.UTF8.GetBytes("a note");

        using var response = await anna.SendAsync(Upload(Attachment, bytes, "text/plain", "note.txt"), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var id = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("attachments/file", await StoredAsync(id, "field_path"));
        Assert.Equal("text/plain", await StoredAsync(id, "media_type"));
    }

    [Fact]
    public async Task A_part_without_a_content_type_is_stored_as_octet_stream_and_the_name_is_cleaned()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var longName = new string('a', 300);

        using var response = await anna.SendAsync(Upload(Attachment, [1, 2, 3], null, $"../etc/{longName}"), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("application/octet-stream", body.RootElement.GetProperty("mediaType").GetString());
        Assert.Equal(255, body.RootElement.GetProperty("name").GetString()!.Length);
        Assert.All(body.RootElement.GetProperty("name").GetString()!, character => Assert.Equal('a', character));
    }

    [Theory]
    [InlineData("rita")]
    [InlineData("fia")]
    [InlineData("binh")]
    public async Task A_user_whose_grants_do_not_cover_the_field_gets_403_and_nothing_is_stored(string user)
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        var before = await FileCountAsync();

        using var receipt = await client.SendAsync(Upload(Receipt, Pdf(10), "application/pdf"), CancellationToken);
        using var attachment = await client.SendAsync(Upload(Attachment, Pdf(10), "application/pdf"), CancellationToken);

        using (await ReadProblemAsync(receipt, HttpStatusCode.Forbidden))
        {
        }

        using (await ReadProblemAsync(attachment, HttpStatusCode.Forbidden))
        {
        }

        Assert.Equal(before, await FileCountAsync());
    }

    [Fact]
    public async Task A_user_without_a_grant_learns_nothing_from_the_body()
    {
        using var rita = fixture.CreateClient().SignedInAs("rita");

        // The grant comes before the content type and the body.
        using var notMultipart = await rita.SendAsync(Request(HttpMethod.Post, Receipt, HostA, "{}", "application/json"), CancellationToken);
        using var oversized = await rita.SendAsync(Upload(Receipt, Pdf(5000), "application/pdf"), CancellationToken);

        using (await ReadProblemAsync(notMultipart, HttpStatusCode.Forbidden))
        {
        }

        using (await ReadProblemAsync(oversized, HttpStatusCode.Forbidden))
        {
        }
    }

    [Fact]
    public async Task A_request_without_a_session_gets_401()
    {
        using var anonymous = fixture.CreateClient();

        using var response = await anonymous.SendAsync(Upload(Receipt, Pdf(10), "application/pdf"), CancellationToken);

        using (await ReadProblemAsync(response, HttpStatusCode.Unauthorized))
        {
        }
    }

    [Theory]
    [InlineData("/api/apps/FilesApp/entities/Claim/fields/title/files")]
    [InlineData("/api/apps/FilesApp/entities/Claim/fields/missing/files")]
    [InlineData("/api/apps/FilesApp/entities/Claim/fields/attachments/note/files")]
    [InlineData("/api/apps/FilesApp/entities/Claim/fields/title/file/files")]
    [InlineData("/api/apps/FilesApp/entities/ClaimAttachment/fields/file/files")]
    [InlineData("/api/apps/FilesApp/entities/Missing/fields/receipt/files")]
    [InlineData("/api/apps/Missing/entities/Claim/fields/receipt/files")]
    public async Task A_path_that_names_no_file_field_gets_404(string path)
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var response = await anna.SendAsync(Upload(path, Pdf(10), "application/pdf"), CancellationToken);

        using (await ReadProblemAsync(response, HttpStatusCode.NotFound))
        {
        }
    }

    [Fact]
    public async Task A_file_over_the_field_limit_gets_413_and_one_at_the_limit_is_stored()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var before = await FileCountAsync();

        using var over = await anna.SendAsync(Upload(Receipt, Pdf(1025), "application/pdf"), CancellationToken);
        using (await ReadProblemAsync(over, HttpStatusCode.RequestEntityTooLarge))
        {
        }

        Assert.Equal(before, await FileCountAsync());

        using var exact = await anna.SendAsync(Upload(Receipt, Pdf(1024), "application/pdf"), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);
    }

    [Fact]
    public async Task A_media_type_that_accept_refuses_gets_415()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var before = await FileCountAsync();

        using var response = await anna.SendAsync(Upload(Receipt, Encoding.UTF8.GetBytes("text"), "text/plain", "a.txt"), CancellationToken);

        using (await ReadProblemAsync(response, HttpStatusCode.UnsupportedMediaType))
        {
        }

        Assert.Equal(before, await FileCountAsync());
    }

    [Fact]
    public async Task A_subtype_wildcard_and_media_type_parameters_are_accepted()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];

        using var response = await anna.SendAsync(Upload(Receipt, png, "IMAGE/PNG; charset=binary", "a.png"), CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("image/png", body.RootElement.GetProperty("mediaType").GetString());
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, "application/pdf")]
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 }, "text/plain")]
    [InlineData(new byte[] { 0xCF, 0xFA, 0xED, 0xFE, 0x07 }, "application/octet-stream")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, "application/pdf")]
    public async Task A_file_that_starts_like_an_executable_or_not_like_its_type_gets_415(byte[] bytes, string mediaType)
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var before = await FileCountAsync();

        // An executable is refused for any declared type, and the attachment field accepts any type.
        using var attachment = await anna.SendAsync(Upload(Attachment, bytes, mediaType, "invoice.pdf"), CancellationToken);
        using var receipt = await anna.SendAsync(Upload(Receipt, bytes, "application/pdf", "invoice.pdf"), CancellationToken);

        using (await ReadProblemAsync(attachment, HttpStatusCode.UnsupportedMediaType))
        {
        }

        using (await ReadProblemAsync(receipt, HttpStatusCode.UnsupportedMediaType))
        {
        }

        Assert.Equal(before, await FileCountAsync());
    }

    [Fact]
    public async Task A_request_that_is_not_multipart_gets_415()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");

        using var json = await anna.SendAsync(Request(HttpMethod.Post, Receipt, HostA, "{}", "application/json"), CancellationToken);
        using var noBoundary = await anna.SendAsync(Request(HttpMethod.Post, Receipt, HostA, "x", "multipart/form-data"), CancellationToken);

        using (await ReadProblemAsync(json, HttpStatusCode.UnsupportedMediaType))
        {
        }

        using (await ReadProblemAsync(noBoundary, HttpStatusCode.UnsupportedMediaType))
        {
        }
    }

    [Fact]
    public async Task A_missing_misnamed_nameless_empty_or_extra_part_gets_400()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var before = await FileCountAsync();

        var noParts = Request(HttpMethod.Post, Receipt, HostA);
        noParts.Content = new MultipartFormDataContent();
        using var missing = await anna.SendAsync(noParts, CancellationToken);
        using var misnamed = await anna.SendAsync(Upload(Receipt, Pdf(10), "application/pdf", "a.pdf", "other"), CancellationToken);
        using var nameless = await anna.SendAsync(Upload(Receipt, Pdf(10), "application/pdf", null), CancellationToken);
        using var empty = await anna.SendAsync(Upload(Receipt, [], "application/pdf"), CancellationToken);
        var twoParts = Upload(Receipt, Pdf(10), "application/pdf");
        ((MultipartFormDataContent)twoParts.Content!).Add(new ByteArrayContent(Pdf(10)), "file", "second.pdf");
        using var extra = await anna.SendAsync(twoParts, CancellationToken);

        foreach (var response in new[] { missing, misnamed, nameless, empty, extra })
        {
            using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
            Assert.Equal("file", Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject()).Name);
        }

        Assert.Equal(before, await FileCountAsync());
    }

    [Fact]
    public async Task A_file_field_is_a_uuid_column_with_a_foreign_key_to_the_files_table()
    {
        var tables = await fixture.ScalarAsync(
            TenantA,
            """
            SELECT string_agg(format('%s:%s:%s', c.column_name, c.data_type, coalesce(
                (SELECT ccu.table_schema || '.' || ccu.table_name
                 FROM information_schema.key_column_usage kcu
                 JOIN information_schema.referential_constraints rc ON rc.constraint_name = kcu.constraint_name AND rc.constraint_schema = kcu.constraint_schema
                 JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = rc.unique_constraint_name AND ccu.constraint_schema = rc.unique_constraint_schema
                 WHERE kcu.table_schema = c.table_schema AND kcu.table_name = c.table_name AND kcu.column_name = c.column_name), '')), ',' ORDER BY c.column_name)
            FROM information_schema.columns c
            WHERE c.table_schema = 'entities' AND c.column_name IN ('f_receipt', 'f_file')
            """);

        var text = (string)tables!;
        Assert.Equal(2, text.Split(',').Length);
        Assert.All(text.Split(','), column => Assert.EndsWith(":uuid:axis.files", column, StringComparison.Ordinal));
    }
}
