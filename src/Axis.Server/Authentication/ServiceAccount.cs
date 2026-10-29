namespace Axis.Server.Authentication;

/// <summary>
/// Another system that calls the record, data source, process start, process inspection and operator command routes with a bearer token from its tenant's identity provider, as a service
/// account under the same policies as a user. A route opts in with <see cref="ServiceAccountRoute"/>. On such a route a
/// request with a bearer or DPoP token is a service account request: the token is judged against <c>Oidc:ServiceAudience</c>,
/// the session cookie is never read or renewed, and no CSRF header is needed. Every other route ignores the token.
/// </summary>
internal static class ServiceAccount
{
    /// <summary>Forwards to the service bearer scheme of the request's tenant.</summary>
    public const string Scheme = "Axis.ServiceAccount";

    public const string SchemePrefix = "service-";

    public static string SchemeName(string tenantId) => $"{SchemePrefix}{tenantId}";

    /// <summary>
    /// Whether the request is a service account request: its route carries <see cref="ServiceAccountRoute"/> and it
    /// has an <c>Authorization: Bearer</c> or <c>Authorization: DPoP</c> header, in any letter case. A tenant under
    /// <c>fapi2</c> takes only the second.
    /// </summary>
    public static bool Applies(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ServiceAccountRoute>() is null)
        {
            return false;
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || authorization.StartsWith("DPoP ", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The endpoint metadata that marks a route a service account may reach. The scheme choice, the CSRF exemption and the
/// route test all read it, so none of them matches paths.
/// </summary>
internal sealed class ServiceAccountRoute
{
    public static readonly ServiceAccountRoute Instance = new();

    private ServiceAccountRoute()
    {
    }
}
