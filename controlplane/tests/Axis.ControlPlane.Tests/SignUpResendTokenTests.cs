using Axis.ControlPlane.SignUp;

namespace Axis.ControlPlane.Tests;

public sealed class SignUpResendTokenTests
{
    private static readonly byte[] _key = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();
    private static readonly DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private static SignUpResendToken.Payload PayloadOf(Guid signUpId, DateTimeOffset? expiresAt = null) =>
        new("ana@example.com", "Ana Nguyen", "en", signUpId, expiresAt ?? _now + SignUpResendToken.Lifetime);

    [Fact]
    public void A_token_reads_back_its_values()
    {
        var id = Guid.CreateVersion7();
        var token = SignUpResendToken.Create(_key, PayloadOf(id));

        var read = SignUpResendToken.Read(_key, token, _now);

        Assert.Equal(PayloadOf(id), read);
    }

    [Fact]
    public void A_token_of_a_trial_used_email_reads_back_an_empty_sign_up_id()
    {
        var token = SignUpResendToken.Create(_key, PayloadOf(Guid.Empty));

        Assert.Equal(Guid.Empty, SignUpResendToken.Read(_key, token, _now)!.SignUpId);
    }

    [Fact]
    public void A_token_for_a_confirm_email_and_one_for_a_trial_used_email_have_one_length_and_differ_each_time()
    {
        var confirm = SignUpResendToken.Create(_key, PayloadOf(Guid.CreateVersion7()));
        var trialUsed = SignUpResendToken.Create(_key, PayloadOf(Guid.Empty));

        Assert.Equal(confirm.Length, trialUsed.Length);
        Assert.NotEqual(confirm, SignUpResendToken.Create(_key, PayloadOf(Guid.CreateVersion7())));
    }

    [Fact]
    public void A_token_with_one_changed_character_reads_as_null()
    {
        var token = SignUpResendToken.Create(_key, PayloadOf(Guid.CreateVersion7()));
        var middle = token.Length / 2;
        var changed = token[..middle] + (token[middle] == 'A' ? 'B' : 'A') + token[(middle + 1)..];

        Assert.NotNull(SignUpResendToken.Read(_key, token, _now));
        Assert.Null(SignUpResendToken.Read(_key, changed, _now));
    }

    [Fact]
    public void A_token_sealed_with_another_key_reads_as_null()
    {
        var token = SignUpResendToken.Create(_key, PayloadOf(Guid.CreateVersion7()));
        var other = _key.Select(value => (byte)(value + 1)).ToArray();

        Assert.Null(SignUpResendToken.Read(other, token, _now));
    }

    [Fact]
    public void A_token_reads_as_null_from_its_expiry_on_and_as_values_one_second_before()
    {
        var expiresAt = _now + TimeSpan.FromHours(1);
        var token = SignUpResendToken.Create(_key, PayloadOf(Guid.CreateVersion7(), expiresAt));

        Assert.NotNull(SignUpResendToken.Read(_key, token, expiresAt - TimeSpan.FromSeconds(1)));
        Assert.Null(SignUpResendToken.Read(_key, token, expiresAt));
        Assert.Null(SignUpResendToken.Read(_key, token, expiresAt + TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("not base64url!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void A_missing_or_malformed_token_reads_as_null(string? token)
    {
        Assert.Null(SignUpResendToken.Read(_key, token, _now));
    }
}
