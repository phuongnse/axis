using Microsoft.AspNetCore.Hosting;

namespace Axis.Server.Tests;

/// <summary>Gives test hosts the Oidc block that startup requires for every tenant.</summary>
internal static class TestOidc
{
    /// <summary>
    /// Points each tenant at an identity provider on a port that refuses connections. The server
    /// reads the discovery document only when someone signs in or out, so the host still starts.
    /// The authority uses https, so the settings also hold outside Development and Testing.
    /// </summary>
    public static IWebHostBuilder UseTestOidc(this IWebHostBuilder builder, params string[] tenantIds)
    {
        foreach (var tenantId in tenantIds)
        {
            builder.UseSetting($"Tenants:{tenantId}:Oidc:Authority", "https://127.0.0.1:1/realms/axis");
            builder.UseSetting($"Tenants:{tenantId}:Oidc:ClientId", "axis");
            builder.UseSetting($"Tenants:{tenantId}:Oidc:ClientSecret", "test");
            builder.UseSetting($"Tenants:{tenantId}:Oidc:ServiceAudience", "axis-services");
        }

        return builder;
    }
}
