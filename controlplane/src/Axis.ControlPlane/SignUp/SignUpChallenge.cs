using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The ALTCHA proof-of-work challenge of the sign-up page, see <c>docs/reference/sign-up.md#bots-and-abuse</c>. The browser finds the
/// number that hashes to the challenge, and the signature lets the control plane tell a challenge it served from one a caller made up.
/// </summary>
internal static partial class SignUpChallenge
{
    /// <summary>The fewest bytes of the key that signs the challenges.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The most guesses a solution can take. A phone solves it in about a second, while a bot pays for each try.</summary>
    public const int MaxNumber = 100_000;

    /// <summary>How long after it is served a challenge expires.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private const int SaltBytes = 12;

    public static object Create(byte[] key, DateTimeOffset now)
    {
        var salt = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SaltBytes))}?expires={(now + Lifetime).ToUnixTimeSeconds()}&";
        var number = RandomNumberGenerator.GetInt32(0, MaxNumber + 1);
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}{number}")));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challenge)));
        return new { algorithm = "SHA-256", challenge, maxnumber = MaxNumber, salt, signature };
    }

    /// <summary>
    /// Checks the solution a sign-up carries as <c>altcha</c>, the base64 of the JSON that the page builds, see
    /// <c>docs/reference/sign-up.md#bots-and-abuse</c>. The answer is the SHA-256 challenge and the time the solution expires, or null
    /// when it is malformed, wrong, signed with another key or expired. It does not tell whether the solution was used before.
    /// </summary>
    public static (byte[] Challenge, DateTimeOffset ExpiresAt)? Verify(byte[] key, string? altcha, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(altcha))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(Convert.FromBase64String(altcha)) is not JsonObject payload
                || Text(payload, "algorithm") != "SHA-256"
                || Text(payload, "challenge") is not { } challenge
                || Text(payload, "salt") is not { } salt
                || Text(payload, "signature") is not { } signature
                || payload["number"] is not JsonValue numberValue
                || !numberValue.TryGetValue<long>(out var number)
                || number is < 0 or > MaxNumber)
            {
                return null;
            }

            // The salt ends with "&", so a caller cannot move a digit of the expiry into the number: salt + "1" with number 2345
            // would hash like salt with 12345 and push the expiry centuries away.
            var shape = SaltShape().Match(salt);
            if (!shape.Success
                || !long.TryParse(shape.Groups["expires"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                || expires > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                return null;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expires);
            if (expiresAt <= now)
            {
                return null;
            }

            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{salt}{number}")));
            var claimed = Convert.FromHexString(challenge);
            var signed = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challenge));
            if (!CryptographicOperations.FixedTimeEquals(expected, claimed)
                || !CryptographicOperations.FixedTimeEquals(signed, Convert.FromHexString(signature)))
            {
                return null;
            }

            return (expected, expiresAt);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // The salt as Create serves it: the random hex, then the expiry, and the "&" that closes it.
    [GeneratedRegex(@"^[0-9a-f]{24}\?expires=(?<expires>[0-9]{1,12})&$", RegexOptions.CultureInvariant)]
    private static partial Regex SaltShape();

    // The text of a field that is a string, or null.
    private static string? Text(JsonObject payload, string name) =>
        payload[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
