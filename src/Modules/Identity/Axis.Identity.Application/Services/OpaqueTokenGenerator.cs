using System.Security.Cryptography;
using System.Text;

namespace Axis.Identity.Application.Services;

/// <summary>Generates opaque one-time tokens stored as SHA-256 hashes.</summary>
public static class OpaqueTokenGenerator
{
    public static (string RawToken, string TokenHash) Create()
    {
        string rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string tokenHash = Hash(rawToken);
        return (rawToken, tokenHash);
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
