using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@EXAMPLE.COM")]
    [InlineData("first.last+tag@sub.domain.io")]
    public void Email_WhenValueIsValid_IsCreatedSuccessfully(string value)
    {
        Result<Email> result = Email.Create(value);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Email_WhenCreated_IsNormalizedToLowercase()
    {
        Result<Email> result = Email.Create("User@Example.COM");

        result.Value.Value.Should().Be("user@example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("@nodomain.com")]
    [InlineData("noatsign.com")]
    public void Email_WhenValueIsInvalid_ReturnsFailure(string value)
    {
        Result<Email> result = Email.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().NotBeEmpty();
    }

    [Fact]
    public void Email_WhenValuesAreIdentical_AreEqual()
    {
        Email a = Email.Create("user@example.com").Value;
        Email b = Email.Create("user@example.com").Value;

        a.Should().Be(b);
    }

    [Fact]
    public void Email_WhenValuesDiffer_AreNotEqual()
    {
        Email a = Email.Create("alice@example.com").Value;
        Email b = Email.Create("bob@example.com").Value;

        a.Should().NotBe(b);
    }
}
