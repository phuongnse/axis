using System.Security.Cryptography;
using System.Text;

namespace Axis.Integration.Tests;

/// <summary>
/// The PKCE <c>S256</c> pair (RFC 7636) that a test sends when it signs a person in at a public client of a tenant realm, which
/// refuses an authorization request without it (<c>docs/reference/mcp-server.md#sign-in</c>).
/// </summary>
internal sealed record Pkce(string Verifier, string Challenge)
{
    /// <summary>The authorization request parameters that carry <see cref="Challenge"/>, starting with <c>&amp;</c>.</summary>
    public string Query => $"&code_challenge={Challenge}&code_challenge_method=S256";

    /// <summary>A new random verifier and its challenge.</summary>
    public static Pkce New()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
