using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.SignUp;

namespace Axis.ControlPlane.Tests;

public sealed class SignUpChallengeTests
{
    private static readonly byte[] _key = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
    private static readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact]
    public void A_created_challenge_solved_by_brute_force_passes_and_answers_its_hash_and_expiry()
    {
        var challenge = JsonSerializer.SerializeToElement(SignUpChallenge.Create(_key, _now));
        var number = Solve(challenge);

        var result = SignUpChallenge.Verify(_key, Payload(challenge, number), _now);

        Assert.NotNull(result);
        Assert.Equal(Convert.FromHexString(challenge.GetProperty("challenge").GetString()!), result.Value.Challenge);
        Assert.Equal(_now + SignUpChallenge.Lifetime, result.Value.ExpiresAt);
    }

    [Fact]
    public void A_wrong_number_is_refused_next_to_the_right_one_that_passes()
    {
        var challenge = JsonSerializer.SerializeToElement(SignUpChallenge.Create(_key, _now));
        var number = Solve(challenge);

        Assert.NotNull(SignUpChallenge.Verify(_key, Payload(challenge, number), _now));
        Assert.Null(SignUpChallenge.Verify(_key, Payload(challenge, number + 1), _now));
        Assert.Null(SignUpChallenge.Verify(_key, Payload(challenge, -1), _now));
        Assert.Null(SignUpChallenge.Verify(_key, Payload(challenge, SignUpChallenge.MaxNumber + 1), _now));
    }

    [Fact]
    public void A_signature_made_with_another_key_is_refused()
    {
        var challenge = JsonSerializer.SerializeToElement(SignUpChallenge.Create(_key, _now));
        var number = Solve(challenge);
        var otherKey = Enumerable.Range(1, 32).Select(index => (byte)index).ToArray();

        Assert.Null(SignUpChallenge.Verify(otherKey, Payload(challenge, number), _now));
    }

    [Fact]
    public void Another_algorithm_is_refused()
    {
        var challenge = JsonSerializer.SerializeToElement(SignUpChallenge.Create(_key, _now));
        var number = Solve(challenge);

        Assert.Null(SignUpChallenge.Verify(_key, Payload(challenge, number, algorithm: "SHA-512"), _now));
    }

    [Fact]
    public void A_time_after_the_expiry_is_refused_and_the_moment_of_it_too()
    {
        var challenge = JsonSerializer.SerializeToElement(SignUpChallenge.Create(_key, _now));
        var number = Solve(challenge);
        var altcha = Payload(challenge, number);

        Assert.NotNull(SignUpChallenge.Verify(_key, altcha, _now + SignUpChallenge.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.Null(SignUpChallenge.Verify(_key, altcha, _now + SignUpChallenge.Lifetime));
        Assert.Null(SignUpChallenge.Verify(_key, altcha, _now + SignUpChallenge.Lifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void A_salt_without_an_expiry_is_refused_even_when_it_is_signed()
    {
        const string salt = "0123456789abcdef01234567";
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}7")));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(challenge)));
        var altcha = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { algorithm = "SHA-256", challenge, number = 7, salt, signature }));

        Assert.Null(SignUpChallenge.Verify(_key, altcha, _now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!")]
    [InlineData("bm90IGpzb24=")]
    [InlineData("WzFd")]
    [InlineData("e30=")]
    public void A_missing_blank_non_base64_non_JSON_or_empty_solution_is_refused(string? altcha) =>
        Assert.Null(SignUpChallenge.Verify(_key, altcha, _now));

    // The number that hashes with the salt to the challenge, found the way the page finds it.
    private static int Solve(JsonElement challenge)
    {
        var salt = challenge.GetProperty("salt").GetString()!;
        var target = challenge.GetProperty("challenge").GetString()!;
        for (var number = 0; number <= SignUpChallenge.MaxNumber; number++)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{salt}{number}"))));
            if (hash == target)
            {
                return number;
            }
        }

        throw new InvalidOperationException("The challenge has no solution.");
    }

    private static string Payload(JsonElement challenge, int number, string algorithm = "SHA-256") =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            algorithm,
            challenge = challenge.GetProperty("challenge").GetString(),
            number,
            salt = challenge.GetProperty("salt").GetString(),
            signature = challenge.GetProperty("signature").GetString(),
        }));
}
