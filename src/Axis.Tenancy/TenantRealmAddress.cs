namespace Axis.Tenancy;

/// <summary>Reads the identity provider's address out of a tenant's <c>Oidc</c> authority.</summary>
public static class TenantRealmAddress
{
    /// <summary>
    /// The address of the identity provider, ending with a slash, when <paramref name="authority"/> is an absolute http
    /// or https address whose path ends in <c>/realms/</c> and <paramref name="realm"/>. One trailing slash after the
    /// realm name is accepted. Otherwise <c>null</c>. The result has no query and no fragment.
    /// </summary>
    public static Uri? BaseOf(string? authority, string? realm)
    {
        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(realm)
            || !Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var suffix = "/realms/" + Uri.EscapeDataString(realm);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (uri.AbsolutePath.EndsWith("//", StringComparison.Ordinal) || !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Path = path[..^suffix.Length] + "/",
            Query = "",
            Fragment = "",
        };
        return builder.Uri;
    }

    /// <summary>
    /// The identity provider's address and the realm's name when <paramref name="authority"/> is an absolute http or
    /// https address whose path ends in <c>/realms/</c> and a realm name, with at most one trailing slash. Otherwise
    /// <c>null</c>.
    /// </summary>
    public static (Uri BaseAddress, string Realm)? Split(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority)
            || !Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
        if (segments.Length < 3 || segments[^2] != "realms" || segments[^1].Length == 0)
        {
            return null;
        }

        var realm = Uri.UnescapeDataString(segments[^1]);
        return BaseOf(authority, realm) is { } baseAddress ? (baseAddress, realm) : null;
    }
}
