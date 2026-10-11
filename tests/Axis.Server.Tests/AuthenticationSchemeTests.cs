using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The server signs people in only through each tenant's identity provider. It registers the
/// session cookie scheme and one OpenID Connect scheme per tenant, and nothing else.
/// </summary>
public sealed class AuthenticationSchemeTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    [Fact]
    public async Task A_production_host_registers_only_the_session_cookie_and_the_tenant_oidc_scheme()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });
        // Creating a client starts the host.
        using var client = factory.CreateClient();

        var provider = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = (await provider.GetAllSchemesAsync()).OrderBy(scheme => scheme.Name, StringComparer.Ordinal).ToList();

        Assert.Collection(
            schemes,
            scheme =>
            {
                Assert.Equal("Axis.Session", scheme.Name);
                Assert.Equal(typeof(CookieAuthenticationHandler), scheme.HandlerType);
            },
            scheme =>
            {
                Assert.Equal("oidc-default", scheme.Name);
                Assert.Equal(typeof(OpenIdConnectHandler), scheme.HandlerType);
            });
        Assert.Equal("Axis.Session", (await provider.GetDefaultAuthenticateSchemeAsync())?.Name);
    }
}
