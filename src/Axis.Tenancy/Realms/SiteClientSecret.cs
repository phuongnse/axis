using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Axis.Tenancy.Realms;

/// <summary>
/// The secret of a site client. It is not stored: it is the base64url of the HMAC-SHA256 of the client id under the platform
/// client's secret, so <see cref="RealmSync"/> sets it and the server computes the same value.
/// </summary>
public static class SiteClientSecret
{
    /// <summary>The secret of the site client <paramref name="clientId"/> for a tenant whose platform client has <paramref name="platformSecret"/>.</summary>
    public static string Derive(string platformSecret, string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(platformSecret);
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        return Base64Url.EncodeToString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(platformSecret), Encoding.UTF8.GetBytes(clientId)));
    }
}
