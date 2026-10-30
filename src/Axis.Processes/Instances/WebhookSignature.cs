using System.Security.Cryptography;
using System.Text;

namespace Axis.Processes.Instances;

/// <summary>
/// The identity and the signature of a webhook request, as the Standard Webhooks format has them. The id is the
/// same on every attempt of a delivery, so a receiver can drop duplicates, while the timestamp and with it the
/// signature change.
/// </summary>
internal static class WebhookSignature
{
    /// <summary>The prefix of a secret that the Standard Webhooks libraries expect. The rest is base64.</summary>
    internal const string SecretPrefix = "whsec_";

    /// <summary>
    /// The <c>webhook-id</c> of the delivery of an occurrence to a webhook: both ids in hyphenated form, joined by an
    /// underscore, which keeps the dots of the signed text unambiguous.
    /// </summary>
    internal static string Id(Guid eventId, Guid webhookId) => $"{eventId:D}_{webhookId:D}";

    /// <summary>
    /// The <c>webhook-signature</c> header: for each secret of <paramref name="secretValue"/>, which may hold two
    /// separated by a space, <c>v1,</c> and the base64 HMAC-SHA256 of <c>{id}.{timestamp}.{body}</c>, joined by a space.
    /// </summary>
    internal static string Sign(string id, long timestamp, string body, string secretValue)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(secretValue);

        var signed = Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}");
        var signatures = secretValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(secret => "v1," + Convert.ToBase64String(HMACSHA256.HashData(Key(secret), signed)));
        return string.Join(' ', signatures);
    }

    /// <summary>
    /// The HMAC key of a secret: the base64 decoded rest after a <c>whsec_</c> prefix, otherwise the secret's UTF-8
    /// bytes. A <c>whsec_</c> secret whose rest is not base64 is an <see cref="InvalidOperationException"/>, which
    /// names no part of the secret.
    /// </summary>
    private static byte[] Key(string secret)
    {
        if (!secret.StartsWith(SecretPrefix, StringComparison.Ordinal))
        {
            return Encoding.UTF8.GetBytes(secret);
        }

        try
        {
            return Convert.FromBase64String(secret[SecretPrefix.Length..]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("A secret that starts with 'whsec_' is not followed by base64.");
        }
    }
}
