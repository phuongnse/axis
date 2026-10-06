using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class PresentationEndpointTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string UnknownHost = "unknown.example";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Site_describes_the_locales_and_the_navigation()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/site", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var site = await response.Content.ReadFromJsonAsync<SiteBody>(CancellationToken);
        Assert.NotNull(site);
        Assert.Equal("shell.title", site.TitleKey);
        Assert.Equal("en", site.Locales.Default);
        Assert.Equal("en", site.Locales.Fallback);
        Assert.Contains("en", site.Locales.Available);
        Assert.Contains("vi", site.Locales.Available);
        var home = Assert.Single(site.Navigation, item => item.Path == "/");
        Assert.Equal("shell.nav.home", home.LabelKey);
    }

    [Fact]
    public async Task Texts_return_the_map_of_a_known_locale()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/texts/en", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextsBody>(CancellationToken);
        Assert.Equal("en", body?.Locale);
        Assert.Equal("Welcome to Axis", body?.Texts["shell.home.title"]);
    }

    [Fact]
    public async Task Texts_match_the_locale_ignoring_letter_case()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/texts/VI", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextsBody>(CancellationToken);
        Assert.Equal("vi", body?.Locale);
        Assert.Equal("Chào mừng đến với Axis", body?.Texts["shell.home.title"]);
    }

    [Fact]
    public async Task Texts_of_an_unknown_locale_are_a_404_problem()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/texts/xx", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("No text resources exist for this locale.", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/site")]
    [InlineData("/api/texts/en")]
    public async Task Unknown_host_is_denied(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = UnknownHost;

        var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("No tenant is configured for this host.", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
        });

    private sealed record SiteBody(string Name, string TitleKey, LocalesBody Locales, List<NavigationBody> Navigation);

    private sealed record LocalesBody(string Default, string Fallback, List<string> Available);

    private sealed record NavigationBody(string Key, string Path, string LabelKey);

    private sealed record TextsBody(string Locale, Dictionary<string, string> Texts);
}
