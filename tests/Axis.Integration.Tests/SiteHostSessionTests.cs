using System.Net;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A session stays on the host that made it. A session made on a site's own host answers as no session on the tenant's
/// own host, and the other way round, and the refused request leaves the row alone. A test-only path creates the session,
/// as the OIDC callback does.
/// </summary>
public sealed class SiteHostSessionTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_session_works_only_on_the_host_that_made_it_and_the_refused_request_leaves_its_row()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, OwnHost(id))).Exit);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "host", "add", id, SiteHost(id), "--site", "purchasing"], extra: AdminScenario.Directory)).Exit);
        await using var factory = await CreateServerAsync(scenario);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var siteCookie = await SignInAsync(client, SiteHost(id));
        var ownCookie = await SignInAsync(client, OwnHost(id));

        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, SiteHost(id), siteCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, OwnHost(id), siteCookie));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, OwnHost(id), ownCookie));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(client, SiteHost(id), ownCookie));

        // The refused requests deleted nothing, so each session still works on its own host.
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, SiteHost(id), siteCookie));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(client, OwnHost(id), ownCookie));
        Assert.Equal(["purchasing", null], await SitePathsAsync(scenario, id));
    }

    private static string OwnHost(string id) => $"{id}.example.test";

    private static string SiteHost(string id) => $"purchasing.{id}.example.test";

    private static async Task<WebApplicationFactory<Program>> CreateServerAsync(AdminScenario scenario)
    {
        // An hour, so the timer never races the refresh that the test triggers itself.
        var factory = AdminScenario.CreateServer(scenario.Platform).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenancy:RefreshInterval", "01:00:00");
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new SessionStartupFilter()));
        });
        await factory.Services.GetRequiredService<TenantDirectoryRefresh>().RefreshAsync(CancellationToken);
        return factory;
    }

    private static async Task<string> SignInAsync(HttpClient client, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/test/session", UriKind.Relative));
        request.Headers.Host = host;
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = Session.Scheme + "=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client, string host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        using var response = await client.SendAsync(request, CancellationToken);
        return response.StatusCode;
    }

    /// <summary>The <c>site_path</c> of every session row of the tenant, in order with the null last.</summary>
    private static async Task<string?[]> SitePathsAsync(AdminScenario scenario, string id)
    {
        var row = await scenario.RowAsync(id);
        var password = AdminScenario.Cipher.Decrypt(id, Axis.Tenancy.Platform.TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        await using var connection = new NpgsqlConnection(scenario.TenantConnectionString(id, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT site_path FROM axis.sessions ORDER BY site_path NULLS LAST", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var paths = new List<string?>();
        while (await reader.ReadAsync(CancellationToken))
        {
            paths.Add(await reader.IsDBNullAsync(0, CancellationToken) ? null : reader.GetString(0));
        }

        return [.. paths];
    }

    /// <summary>Answers <c>GET /test/session</c> by signing requester in to the tenant of the request host.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        var tenantId = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId;
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, "requester", "Requester User", ["employee"]));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
