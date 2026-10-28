using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

/// <summary>
/// The server lists no static folder and answers no <c>TRACE</c> request. The host serves a fake web root from a
/// temporary content root, because the real one loads the Development settings.
/// </summary>
public sealed class StaticFolderAndTraceTests : IAsyncLifetime
{
    private const string Asset = "/assets/index-abc123.js";
    private const string Page = "<!doctype html><title>Axis</title>";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"axis-static-folder-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/assets/")]
    [InlineData("/assets")]
    public async Task A_static_folder_answers_no_listing_of_its_files(string path)
    {
        using var client = _factory!.CreateClient();

        var response = await client.GetAsync(path, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.NotFound,
            $"{path} answered {(int)response.StatusCode}.");
        Assert.DoesNotContain("index-abc123.js", body, StringComparison.Ordinal);
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            Assert.Equal(Page, body);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData(Asset)]
    [InlineData("/api/site")]
    public async Task A_TRACE_request_is_not_a_success_and_is_not_echoed(string path)
    {
        using var client = _factory!.CreateClient();
        var probe = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(new HttpMethod("TRACE"), path);
        request.Headers.Add("X-Trace-Probe", probe);

        var response = await client.SendAsync(request, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.False(response.IsSuccessStatusCode, $"TRACE {path} answered {(int)response.StatusCode}.");
        Assert.DoesNotContain(probe, body, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Trace-Probe"));
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), Page);
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc123.js"), "export {};");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(_contentRoot);
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", TestPlatform.UnreachableDatabase);
            builder.UseTestOidc("a");
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }
}
