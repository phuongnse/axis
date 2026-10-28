using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A 25 MB file uploads and downloads without the server holding it in memory. Each of the two operations
/// takes the best of three attempts after one warm-up, and the best attempt finishes within the time limit and
/// allocates fewer bytes than the file's size. The allocation is counted across the whole process, so the
/// server and the test client are both in it. The server is a Kestrel host on a local port, because the in-process
/// test server copies a request or a response body whole, which would show as an allocation of the file's size. For the same reason the client does not follow redirects.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class FilePerformanceTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>, IDisposable
{
    private const int FileSize = 26_214_400;
    private const int Attempts = 3;
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";
    private const string Attachment = "/api/apps/FilesApp/entities/Claim/fields/attachments/file/files";

    // A 25 MB file is about 25 ms of copying in memory and a little more through PostgreSQL, so each operation is
    // expected to take well under a second. Five seconds leaves room for a shared CI runner and still catches a
    // slowdown of an order of magnitude.
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(5);

    private readonly WebApplicationFactory<Program> _kestrel = StartKestrel(fixture);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<Program> StartKestrel(RecordApiFixture fixture)
    {
        var factory = fixture.Factory.WithWebHostBuilder(_ => { });
        factory.UseKestrel();
        factory.StartServer();
        return factory;
    }

    // The default client follows redirects, which makes it copy a request body whole to be able to send it again.
    private HttpClient Client() =>
        _kestrel.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = _kestrel.ClientOptions.BaseAddress, AllowAutoRedirect = false, HandleCookies = false });

    public void Dispose() => _kestrel.Dispose();

    private static byte[] Payload()
    {
        var data = new byte[FileSize];
        new Random(76).NextBytes(data);
        "AXIS"u8.CopyTo(data);
        return data;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] data)
    {
        var request = Request(HttpMethod.Post, Attachment, HostA);
        var form = new MultipartFormDataContent();
        var part = new StreamContent(new MemoryStream(data, writable: false));
        part.Headers.TryAddWithoutValidation("Content-Type", "application/octet-stream");
        form.Add(part, "file", "large.bin");
        request.Content = form;
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // The best time and the fewest bytes allocated by one run of the operation, after one run that is not measured.
    private static async Task<(TimeSpan Time, long Allocated)> BestOfAsync(Func<Task> operation)
    {
        await operation();

        var time = TimeSpan.MaxValue;
        var allocated = long.MaxValue;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            var used = GC.GetTotalAllocatedBytes(precise: true) - before;
            time = stopwatch.Elapsed < time ? stopwatch.Elapsed : time;
            allocated = Math.Min(allocated, used);
        }

        return (time, allocated);
    }

    [Fact]
    public async Task A_25_MB_upload_finishes_within_the_time_limit_and_allocates_less_than_the_file()
    {
        var data = Payload();
        using var requester = Client().SignedInAs("requester");

        var (time, allocated) = await BestOfAsync(async () => await UploadAsync(requester, data));

        TestContext.Current.TestOutputHelper?.WriteLine($"A {data.Length}-byte upload took {time} and allocated {allocated} bytes.");
        Assert.True(time < _limit, $"The best upload took {time}.");
        Assert.True(allocated < data.Length, $"The best upload allocated {allocated} bytes for a file of {data.Length}.");
    }

    [Fact]
    public async Task A_25_MB_download_finishes_within_the_time_limit_and_allocates_less_than_the_file()
    {
        var data = Payload();
        using var requester = Client().SignedInAs("requester");
        Guid claim;
        using (var created = await requester.SendAsync(Request(HttpMethod.Post, Claims, HostA, """{ "values": { "title": "Large" } }"""), CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
            claim = body.RootElement.GetProperty("id").GetGuid();
        }

        var file = await UploadAsync(requester, data);
        using (var attached = await requester.SendAsync(
            Request(HttpMethod.Patch, $"{Claims}/{claim:D}", HostA, $$"""{ "version": 1, "values": { "attachments": [{ "file": "{{file:D}}" }] } }"""),
            CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
        }

        var (time, allocated) = await BestOfAsync(async () =>
        {
            using var response = await requester.SendAsync(
                Request($"/api/apps/FilesApp/files/{file:D}", HostA),
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var content = await response.Content.ReadAsStreamAsync(CancellationToken);
            await content.CopyToAsync(Stream.Null, CancellationToken);
        });

        TestContext.Current.TestOutputHelper?.WriteLine($"A {data.Length}-byte download took {time} and allocated {allocated} bytes.");
        Assert.True(time < _limit, $"The best download took {time}.");
        Assert.True(allocated < data.Length, $"The best download allocated {allocated} bytes for a file of {data.Length}.");
    }
}
