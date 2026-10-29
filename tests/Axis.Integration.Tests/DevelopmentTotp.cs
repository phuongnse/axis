using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>
/// The second step of a development user: a time-based one-time password (RFC 6238) that the test computes from the
/// secret in the development realm file, which is the only place that holds the secrets.
/// </summary>
internal static class DevelopmentTotp
{
    private const int Period = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>The Base32 secret of the user's <c>otp</c> credential in <c>axis-realm.json</c>.</summary>
    public static string Secret(string username)
    {
        using var realm = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Keycloak", "axis-realm.json")));
        var user = realm.RootElement.GetProperty("users").EnumerateArray()
            .Single(candidate => candidate.GetProperty("username").GetString() == username);
        var credential = user.GetProperty("credentials").EnumerateArray()
            .Single(candidate => candidate.GetProperty("type").GetString() == "otp");
        using var secretData = JsonDocument.Parse(credential.GetProperty("secretData").GetString()!);
        return secretData.RootElement.GetProperty("value").GetString()!;
    }

    /// <summary>The 6-digit code of the user at <paramref name="at"/>, or now.</summary>
    public static string Code(string username, DateTimeOffset? at = null) => CodeFromSecret(Secret(username), at);

    /// <summary>The 6-digit code for a Base32 secret at <paramref name="at"/>, or now.</summary>
    public static string CodeFromSecret(string base32, DateTimeOffset? at = null)
    {
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / Period;
        var message = new byte[8];
        for (var index = 7; index >= 0; index--)
        {
            message[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

#pragma warning disable CA5350 // RFC 6238 and the realm's OTP policy (HmacSHA1) use SHA-1.
        var hash = HMACSHA1.HashData(DecodeBase32(base32), message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value.TrimEnd('='))
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xff));
            }
        }

        return [.. bytes];
    }
}
