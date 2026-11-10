using System.Security.Cryptography;
using System.Text;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// The ALTCHA proof-of-work challenge of the sign-up page, see <c>docs/reference/sign-up.md#bots-and-abuse</c>. The browser finds the
/// number that hashes to the challenge, and the signature lets the control plane tell a challenge it served from one a caller made up.
/// </summary>
internal static class SignUpChallenge
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
        var salt = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SaltBytes))}?expires={(now + Lifetime).ToUnixTimeSeconds()}";
        var number = RandomNumberGenerator.GetInt32(0, MaxNumber + 1);
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}{number}")));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challenge)));
        return new { algorithm = "SHA-256", challenge, maxnumber = MaxNumber, salt, signature };
    }
}
