using System.Net;
using System.Text.Json;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class SiteHostTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly string _purchasing = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
    private static readonly string _leave = Path.Combine(AppContext.BaseDirectory, "Samples", "leave-requests");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_site_host_serves_only_its_site_and_the_tenants_own_host_still_serves_the_rest()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateServedTenantAsync(scenario);
        await using var factory = await CreateServerAsync(scenario);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).SignedInAs("requester");
        var site = SiteHost(id);
        var own = OwnHost(id);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/sites/purchasing", site));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/me", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/api/sites", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/api/sites/leave", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/api/studio/apps", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/api/apps/LeaveRequests/tasks", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/api/me/sessions", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/studio", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/account", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/users", site));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(client, "/assets/missing.js", site));

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/sites", own));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/sites/leave", own));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(client, "/api/studio/apps", own));
    }

    [Fact]
    public async Task The_tenants_own_host_redirects_the_site_path_to_the_site_host_without_the_path()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateServedTenantAsync(scenario);
        await using var factory = await CreateServerAsync(scenario);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).SignedInAs("requester");
        var target = $"http://{SiteHost(id)}/requests?view=open";

        using var moved = await client.SendAsync(Request("/purchasing/requests?view=open", OwnHost(id)), CancellationToken);
        Assert.Equal(HttpStatusCode.Found, moved.StatusCode);
        Assert.Equal(target, moved.Headers.Location?.ToString());

        using var upper = await client.SendAsync(Request("/PURCHASING", OwnHost(id)), CancellationToken);
        Assert.Equal(HttpStatusCode.Found, upper.StatusCode);
        Assert.Equal($"http://{SiteHost(id)}/", upper.Headers.Location?.ToString());

        // A site without a host of its own stays on the tenant's host.
        using var other = await client.SendAsync(Request("/leave", OwnHost(id)), CancellationToken);
        Assert.NotEqual(HttpStatusCode.Found, other.StatusCode);
    }

    [Fact]
    public async Task A_site_host_serves_the_spa_at_the_root_and_at_unprefixed_pages_and_names_its_site_in_the_site_metadata()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = await CreateServedTenantAsync(scenario);
        await using var factory = await CreateServerAsync(scenario);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).SignedInAs("requester");

        // The test server has no built SPA, so the fallback may answer 404 without a body. The host's refusal is a problem document.
        Assert.False(await IsRefusedAsync(client, "/", SiteHost(id)));
        Assert.False(await IsRefusedAsync(client, "/purchaserequests", SiteHost(id)));
        Assert.False(await IsRefusedAsync(client, "/purchaserequests/new", SiteHost(id)));
        Assert.True(await IsRefusedAsync(client, "/studio", SiteHost(id)));

        Assert.Equal("purchasing", await HostSiteAsync(client, SiteHost(id)));
        Assert.Null(await HostSiteAsync(client, OwnHost(id)));
    }

    private static async Task<bool> IsRefusedAsync(HttpClient client, string path, string host)
    {
        using var response = await client.SendAsync(Request(path, host), CancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType?.MediaType == "application/problem+json";
    }

    private static async Task<string?> HostSiteAsync(HttpClient client, string host)
    {
        using var response = await client.SendAsync(Request("/api/site", host), CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.TryGetProperty("hostSite", out var site) ? site.GetString() : null;
    }

    private static string OwnHost(string id) => $"{id}.example.test";

    private static string SiteHost(string id) => $"purchasing.{id}.example.test";

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string path, string host)
    {
        using var response = await client.SendAsync(Request(path, host), CancellationToken);
        return response.StatusCode;
    }

    private static async Task<WebApplicationFactory<Program>> CreateServerAsync(AdminScenario scenario)
    {
        // An hour, so the timer never races the refresh that the test triggers itself.
        var factory = AdminScenario.CreateServer(scenario.Platform)
            .WithWebHostBuilder(builder => builder.UseSetting("Tenancy:RefreshInterval", "01:00:00"));
        await factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);
        return factory;
    }

    // A tenant with the purchase request and leave request samples active, and a host of its own for the purchasing site.
    private static async Task<string> CreateServedTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, OwnHost(id))).Exit);
        await using (var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(scenario.Platform).Options))
        {
            context.TenantBindings.AddRange(
                new TenantBindingRow { TenantId = id, Name = "PurchasingSystemUrl", Kind = "url", Url = "https://purchasing.example.test" },
                new TenantBindingRow
                {
                    TenantId = id,
                    Name = "PurchasingSystemToken",
                    Kind = "secret",
                    SecretValue = AdminScenario.Cipher.Encrypt(id, TenantSecretFields.Binding("PurchasingSystemToken"), "binding-secret-value"),
                });
            await context.SaveChangesAsync(CancellationToken);
        }

        Assert.Equal(0, (await scenario.RunAsync(["activate", _purchasing, "--tenant", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["activate", _leave, "--tenant", id])).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "host", "add", id, SiteHost(id), "--site", "purchasing"], extra: AdminScenario.Directory)).Exit);
        return id;
    }
}
