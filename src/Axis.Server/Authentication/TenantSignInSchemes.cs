using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Axis.Server.Authentication;

/// <summary>
/// Brings the OIDC client and the endpoint caller scheme of each tenant in line with the tenants served now. A tenant
/// that is served gains both schemes, a tenant that is no longer served loses them, and a tenant whose settings changed
/// builds its options again on their next use, which also fetches the identity provider's metadata again.
/// </summary>
internal sealed class TenantSignInSchemes(
    TenantSignInSettings settings,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitorCache<OpenIdConnectOptions> oidcOptions,
    IOptionsMonitorCache<JwtBearerOptions> bearerOptions)
{
    /// <param name="next">The settings of the tenants served from now on.</param>
    /// <param name="changed">The ids of the tenants that are new or whose settings changed.</param>
    public void Apply(IReadOnlyDictionary<string, TenantOidcSettings> next, IReadOnlyCollection<string> changed)
    {
        var previous = settings.Current;
        var removed = previous.Keys.Where(id => !next.ContainsKey(id)).ToList();
        foreach (var id in removed)
        {
            schemes.RemoveScheme(TenantSignIn.SchemeName(id));
            schemes.RemoveScheme(EndpointCaller.SchemeName(id));
        }

        settings.Replace(next);

        // Dropping the cached options makes the next use build them from the new settings.
        foreach (var id in removed.Concat(changed))
        {
            oidcOptions.TryRemove(TenantSignIn.SchemeName(id));
            bearerOptions.TryRemove(EndpointCaller.SchemeName(id));
        }

        foreach (var id in next.Keys.Where(id => !previous.ContainsKey(id)))
        {
            schemes.TryAddScheme(new AuthenticationScheme(TenantSignIn.SchemeName(id), displayName: null, typeof(OpenIdConnectHandler)));
            schemes.TryAddScheme(new AuthenticationScheme(EndpointCaller.SchemeName(id), displayName: null, typeof(JwtBearerHandler)));
        }
    }
}
