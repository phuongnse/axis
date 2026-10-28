using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Downloading an attached file, on the FilesApp fixture in tenant A. <c>requester</c> may create, update and read
/// claims, <c>fern</c> may read only the claims titled Shared, <c>fia</c> may read only the title of a claim, and
/// <c>manager</c> holds no role. Every refusal after sign-in is the same 404.
/// </summary>
public sealed class FileDownloadTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";
    private const string Receipt = "/api/apps/FilesApp/entities/Claim/fields/receipt/files";
    private const string Attachment = "/api/apps/FilesApp/entities/Claim/fields/attachments/file/files";
    private const string Pdf = "%PDF-1.7\n";
    private const string NotFoundTitle = "No file has this id.";

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

    private static async Task<Guid> UploadAsync(HttpClient client, string path, string content = Pdf, string mediaType = "application/pdf", string fileName = "receipt.pdf")
    {
        using var response = await client.SendAsync(Upload(path, content, mediaType, fileName), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateClaimAsync(HttpClient client, string title)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, Claims, HostA, $$"""{ "values": { "title": "{{title}}" } }"""), CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task UpdateAsync(HttpClient client, Guid claim, long version, string values)
    {
        using var response = await client.SendAsync(
            Request(HttpMethod.Patch, $"{Claims}/{claim:D}", HostA, $$"""{ "version": {{version}}, "values": {{values}} }"""),
            CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient client, string id) =>
        await client.SendAsync(Request($"/api/apps/FilesApp/files/{id}", HostA), CancellationToken);

    private static async Task AssertNotFoundAsync(HttpClient client, string id)
    {
        using var response = await DownloadAsync(client, id);
        using var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound);
        Assert.Equal(NotFoundTitle, problem.RootElement.GetProperty("title").GetString());
    }

    // A claim with a receipt and a child row file, both attached.
    private async Task<(Guid Claim, Guid Receipt, Guid Note)> AttachedClaimAsync(string title)
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester, title);
        var receipt = await UploadAsync(requester, Receipt);
        var note = await UploadAsync(requester, Attachment, "a note", "text/plain", "note.txt");
        await UpdateAsync(requester, claim, 1, $$"""{ "receipt": "{{receipt:D}}", "attachments": [{ "file": "{{note:D}}", "note": "Scan" }] }""");
        return (claim, receipt, note);
    }

    private static async Task AssertAttachmentAsync(HttpResponseMessage response, string content, string mediaType, string name)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(name, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task A_user_whose_read_filter_matches_the_claim_downloads_a_receipt_and_a_child_row_file()
    {
        var (_, receipt, note) = await AttachedClaimAsync("Shared");
        using var fern = fixture.CreateClient().SignedInAs("fern");

        using var receiptResponse = await DownloadAsync(fern, receipt.ToString("D"));
        using var noteResponse = await DownloadAsync(fern, note.ToString("D"));

        await AssertAttachmentAsync(receiptResponse, Pdf, "application/pdf", "receipt.pdf");
        await AssertAttachmentAsync(noteResponse, "a note", "text/plain", "note.txt");
    }

    [Fact]
    public async Task A_user_without_a_session_gets_401()
    {
        var (_, receipt, _) = await AttachedClaimAsync("Shared");
        using var anonymous = fixture.CreateClient();

        using var response = await DownloadAsync(anonymous, receipt.ToString("D"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_user_without_a_read_grant_gets_404()
    {
        var (_, receipt, note) = await AttachedClaimAsync("Shared");
        using var manager = fixture.CreateClient().SignedInAs("manager");

        await AssertNotFoundAsync(manager, receipt.ToString("D"));
        await AssertNotFoundAsync(manager, note.ToString("D"));
    }

    [Fact]
    public async Task A_user_whose_read_filter_excludes_the_claim_gets_404()
    {
        var (_, receipt, note) = await AttachedClaimAsync("Lost");
        using var fern = fixture.CreateClient().SignedInAs("fern");

        await AssertNotFoundAsync(fern, receipt.ToString("D"));
        await AssertNotFoundAsync(fern, note.ToString("D"));
    }

    [Fact]
    public async Task A_user_whose_field_list_leaves_the_file_field_out_gets_404()
    {
        var (_, receipt, note) = await AttachedClaimAsync("Shared");
        using var fia = fixture.CreateClient().SignedInAs("fia");

        await AssertNotFoundAsync(fia, receipt.ToString("D"));
        await AssertNotFoundAsync(fia, note.ToString("D"));
    }

    [Fact]
    public async Task A_pending_file_gets_404_even_for_the_user_who_uploaded_it()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var pending = await UploadAsync(requester, Receipt);

        await AssertNotFoundAsync(requester, pending.ToString("D"));
    }

    [Fact]
    public async Task A_file_that_a_later_update_replaced_gets_404()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester, "Shared");
        var first = await UploadAsync(requester, Receipt);
        await UpdateAsync(requester, claim, 1, $$"""{ "receipt": "{{first:D}}" }""");
        using (var before = await DownloadAsync(requester, first.ToString("D")))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        var second = await UploadAsync(requester, Receipt, Pdf + "second", fileName: "second.pdf");
        await UpdateAsync(requester, claim, 2, $$"""{ "receipt": "{{second:D}}" }""");

        await AssertNotFoundAsync(requester, first.ToString("D"));
        using var after = await DownloadAsync(requester, second.ToString("D"));
        await AssertAttachmentAsync(after, Pdf + "second", "application/pdf", "second.pdf");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-guid")]
    public async Task An_unknown_or_malformed_id_gets_404(string id)
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");

        await AssertNotFoundAsync(requester, id);
        await AssertNotFoundAsync(requester, Guid.NewGuid().ToString("D"));
    }

    [Fact]
    public async Task An_uploaded_html_file_is_downloaded_as_an_attachment()
    {
        const string Html = "<html><script>alert(1)</script></html>";
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester, "Shared");
        var page = await UploadAsync(requester, Attachment, Html, "text/html", "page.html");
        await UpdateAsync(requester, claim, 1, $$"""{ "attachments": [{ "file": "{{page:D}}" }] }""");

        using var response = await DownloadAsync(requester, page.ToString("D"));

        await AssertAttachmentAsync(response, Html, "text/html", "page.html");
    }

    [Fact]
    public async Task A_file_of_several_chunks_downloads_with_the_same_bytes_and_a_matching_content_length()
    {
        var data = new byte[1_048_576];
        new Random(76).NextBytes(data);
        "AXIS"u8.CopyTo(data);
        using var requester = fixture.CreateClient().SignedInAs("requester");
        var claim = await CreateClaimAsync(requester, "Shared");

        var request = Request(HttpMethod.Post, Attachment, HostA);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(data);
        part.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(part, "file", "large.bin");
        request.Content = form;
        Guid file;
        using (var uploaded = await requester.SendAsync(request, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
            using var body = JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync(CancellationToken));
            file = body.RootElement.GetProperty("id").GetGuid();
        }

        await UpdateAsync(requester, claim, 1, $$"""{ "attachments": [{ "file": "{{file:D}}" }] }""");

        using var response = await DownloadAsync(requester, file.ToString("D"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(data.Length, response.Content.Headers.ContentLength);
        Assert.Equal(data, await response.Content.ReadAsByteArrayAsync(CancellationToken));
    }
}
