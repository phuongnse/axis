using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@EXAMPLE.COM")]
    [InlineData("first.last+tag@sub.domain.io")]
    public void Valid_email_is_created_successfully(string value)
    {
        var result = Email.Create(value);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Email_is_normalized_to_lowercase()
    {
        var result = Email.Create("User@Example.COM");

        result.Value.Value.Should().Be("user@example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain.com")]
    [InlineData("noatsign.com")]
    public void Invalid_email_returns_failure(string value)
    {
        var result = Email.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeEmpty();
    }

    [Fact]
    public void Same_emails_are_equal()
    {
        var a = Email.Create("user@example.com").Value;
        var b = Email.Create("user@example.com").Value;

        a.Should().Be(b);
    }

    [Fact]
    public void Different_emails_are_not_equal()
    {
        var a = Email.Create("alice@example.com").Value;
        var b = Email.Create("bob@example.com").Value;

        a.Should().NotBe(b);
    }
}
