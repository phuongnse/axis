using Axis.Core.Tenancy;
using Axis.Tenancy;

namespace Axis.Server.Authentication;

/// <summary>
/// The <c>WWW-Authenticate</c> header of a 401 on a route that takes a token from another system. A tenant under
/// <c>fapi2</c> says <c>DPoP</c> with the proof algorithms it accepts, and any other tenant says <c>Bearer</c>. The
/// header never says which check failed. A route that has protected-resource metadata adds its URL as
/// <c>resource_metadata</c>, so a client can find the authorization server.
/// </summary>
internal static class BearerChallenge
{
    public static void Write(HttpContext context, string? resourceMetadata = null)
    {
        var current = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
        var profile = current is null ? null : context.RequestServices.GetRequiredService<TenantSignInSettings>().Find(current.TenantId)?.Profile;
        var metadata = resourceMetadata is null ? string.Empty : $"resource_metadata=\"{resourceMetadata}\"";
        context.Response.Headers.WWWAuthenticate = profile == TenantSignInProfiles.Fapi2
            ? $"DPoP algs=\"{string.Join(' ', DpopProof.Algorithms)}\"{(metadata.Length > 0 ? ", " + metadata : string.Empty)}"
            : metadata.Length > 0 ? $"Bearer {metadata}" : "Bearer";
    }
}
