using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Axis.PurchasingStub;

/// <summary>
/// Checks the Standard Webhooks signature of a delivery. It is a copy of the platform's signing rules, so the stub
/// accepts exactly what the worker sends, and it references nothing under <c>src/</c>.
/// </summary>
internal static class WebhookVerifier
{
    private const string SecretPrefix = "whsec_";
    private const string SignaturePrefix = "v1,";
    private const long ToleranceSeconds = 5 * 60;

    // An HMAC-SHA256 is 32 bytes. A longer signature cannot match, so the buffer only has to hold the expected size.
    private const int MaxSignatureBytes = 64;

    public static bool IsValid(string id, string timestamp, string signatureHeader, string body, byte[] key, DateTimeOffset now)
    {
        if (id.Length == 0 || timestamp.Length == 0 || signatureHeader.Length == 0)
        {
            return false;
        }

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var nowSeconds = now.ToUnixTimeSeconds();
        if (seconds < nowSeconds - ToleranceSeconds || seconds > nowSeconds + ToleranceSeconds)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        Span<byte> given = stackalloc byte[MaxSignatureBytes];
        foreach (var entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!entry.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (Convert.TryFromBase64String(entry[SignaturePrefix.Length..], given, out var written)
                && CryptographicOperations.FixedTimeEquals(given[..written], expected))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The HMAC key of a secret: the base64 decoded rest after <c>whsec_</c>, otherwise the UTF-8 bytes.</summary>
    public static byte[] Key(string secret) =>
        secret.StartsWith(SecretPrefix, StringComparison.Ordinal)
            ? Convert.FromBase64String(secret[SecretPrefix.Length..])
            : Encoding.UTF8.GetBytes(secret);
}
