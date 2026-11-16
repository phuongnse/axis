using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The opaque <c>{ resend }</c> token that <c>POST /api/sign-up</c> answers, see <c>docs/reference/sign-up.md#verifying-the-email-first</c>
/// and <c>docs/reference/sign-up.md#check-your-inbox</c>. It holds the person's email, name and language, the id of the pending sign-up
/// (empty when none was stored) and the time it expires, sealed with AES-GCM under a key derived from the challenge key. A token for a new
/// email and one for an email that already had a trial hold the same fields, so a caller cannot tell them apart. The token is never
/// logged and never stored.
/// </summary>
internal static class SignUpResendToken
{
    /// <summary>How long after the sign-up a token works.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;

    /// <summary>What a token holds. <see cref="SignUpId"/> is empty when the sign-up stored no pending sign-up.</summary>
    public sealed record Payload(string Email, string FullName, string Language, Guid SignUpId, DateTimeOffset ExpiresAt);

    // The wire form of the payload: the expiry in whole seconds, so every token of one input has one length.
    private sealed record Wire(string Email, string FullName, string Language, Guid SignUpId, long ExpiresAt);

    /// <summary>The token of a payload, base64url of the nonce, the ciphertext and the tag.</summary>
    public static string Create(byte[] challengeKey, Payload payload)
    {
        ArgumentNullException.ThrowIfNull(challengeKey);
        ArgumentNullException.ThrowIfNull(payload);

        var plain = JsonSerializer.SerializeToUtf8Bytes(new Wire(payload.Email, payload.FullName, payload.Language, payload.SignUpId, payload.ExpiresAt.ToUnixTimeSeconds()));
        var sealedBytes = new byte[NonceBytes + plain.Length + TagBytes];
        var nonce = sealedBytes.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Key(challengeKey), TagBytes);
        aes.Encrypt(nonce, plain, sealedBytes.AsSpan(NonceBytes, plain.Length), sealedBytes.AsSpan(NonceBytes + plain.Length, TagBytes));
        return Base64Url.EncodeToString(sealedBytes);
    }

    /// <summary>
    /// What a token holds, or null when it is missing, malformed, changed, sealed with another key or expired. A caller answers all of
    /// them alike.
    /// </summary>
    public static Payload? Read(byte[] challengeKey, string? token, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(challengeKey);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(token);
            if (bytes.Length <= NonceBytes + TagBytes)
            {
                return null;
            }

            var plain = new byte[bytes.Length - NonceBytes - TagBytes];
            using var aes = new AesGcm(Key(challengeKey), TagBytes);
            aes.Decrypt(bytes.AsSpan(0, NonceBytes), bytes.AsSpan(NonceBytes, plain.Length), bytes.AsSpan(NonceBytes + plain.Length, TagBytes), plain);

            var wire = JsonSerializer.Deserialize<Wire>(plain);
            if (wire is null || wire.Email is null || wire.FullName is null || wire.Language is null)
            {
                return null;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(wire.ExpiresAt);
            return expiresAt <= now ? null : new Payload(wire.Email, wire.FullName, wire.Language, wire.SignUpId, expiresAt);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    // A key of its own, so the challenge signature and the token never share one.
    private static byte[] Key(byte[] challengeKey) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, challengeKey, KeyBytes, info: "axis sign-up resend"u8.ToArray());
}
