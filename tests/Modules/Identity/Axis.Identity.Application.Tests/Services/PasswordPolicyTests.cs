using Axis.Identity.Application.Services;
using FluentAssertions;

namespace Axis.Identity.Application.Tests.Services;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("passwordpassword")]
    [InlineData("1234567890123456790")]
    [InlineData("98765432109876543210")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("abcabcabcabcabc")]
    [InlineData("qwertyuiopasdfgh")]
    public void Validate_WhenPasswordIsNotAcceptable_ReturnsMessage(string password)
    {
        string? result = PasswordPolicy.Validate(password);

        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_WhenPasswordIsLongPhrase_ReturnsNull()
    {
        string? result = PasswordPolicy.Validate("maple river sunrise");

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_WhenPasswordMatchesEmail_ReturnsMessage()
    {
        string? result = PasswordPolicy.Validate(
            "alice@example.com",
            "alice@example.com");

        result.Should().Be(PasswordPolicy.CommonPasswordMessage);
    }

    [Fact]
    public void Validate_WhenPasswordIsTooLong_ReturnsMessage()
    {
        string? result = PasswordPolicy.Validate(new string('a', PasswordPolicy.MaximumLength + 1));

        result.Should().Be(PasswordPolicy.TooLongMessage);
    }
}
