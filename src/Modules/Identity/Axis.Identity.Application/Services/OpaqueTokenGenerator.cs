using System.Security.Cryptography;
using System.Text;

namespace Axis.Identity.Application.Services;

/// <summary>Generates opaque one-time tokens stored as SHA-256 hashes (password reset, email verification).</summary>
public static class OpaqueTokenGenerator
{
    public static (string RawToken, string TokenHash) Create()
    {
        string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string tokenHash = Hash(rawToken);
        return (rawToken, tokenHash);
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
