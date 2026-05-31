using System.Security.Claims;
using Axis.Api.Endpoints;
using Axis.Identity.Domain;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

public class ExternalAuthClaimsExtractorTests
{
    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
    {
        ClaimsIdentity identity = new(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestProvider");
        return new ClaimsPrincipal(identity);
    }

    private static (string Type, string Value) Key(string value) => (ClaimTypes.NameIdentifier, value);
    private static (string Type, string Value) Email(string value) => (ClaimTypes.Email, value);
    private static (string Type, string Value) Verified(string value) => ("email_verified", value);

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void Extract_Google_HonorsEmailVerifiedClaim(string claimValue, bool expected)
    {
        ExternalAuthClaims? result = ExternalAuthClaimsExtractor.Extract(
            Principal(Key("g-1"), Email("user@gmail.com"), Verified(claimValue)),
            ExternalIdentityProvider.Google);

        result.Should().NotBeNull();
        result!.HasVerifiedEmail.Should().Be(expected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Extract_GitHub_HonorsEmailVerifiedClaim(string claimValue, bool expected)
    {
        ExternalAuthClaims? result = ExternalAuthClaimsExtractor.Extract(
            Principal(Key("gh-1"), Email("user@example.com"), Verified(claimValue)),
            ExternalIdentityProvider.GitHub);

        result.Should().NotBeNull();
        result!.HasVerifiedEmail.Should().Be(expected);
    }

    [Fact]
    public void Extract_GitHub_WithoutVerifiedClaim_FailsClosed()
    {
        // Regression: an unverified GitHub email must never be treated as verified.
        ExternalAuthClaims? result = ExternalAuthClaimsExtractor.Extract(
            Principal(Key("gh-2"), Email("attacker@example.com")),
            ExternalIdentityProvider.GitHub);

        result.Should().NotBeNull();
        result!.HasVerifiedEmail.Should().BeFalse();
    }

    [Fact]
    public void Extract_Microsoft_IsTreatedAsVerified()
    {
        ExternalAuthClaims? result = ExternalAuthClaimsExtractor.Extract(
            Principal(Key("ms-1"), Email("user@outlook.com")),
            ExternalIdentityProvider.Microsoft);

        result.Should().NotBeNull();
        result!.HasVerifiedEmail.Should().BeTrue();
    }

    [Fact]
    public void Extract_WhenProviderKeyMissing_ReturnsNull()
    {
        ExternalAuthClaimsExtractor.Extract(
                Principal(Email("user@example.com"), Verified("true")),
                ExternalIdentityProvider.Google)
            .Should().BeNull();
    }

    [Fact]
    public void Extract_WhenEmailMissing_ReturnsNull()
    {
        ExternalAuthClaimsExtractor.Extract(
                Principal(Key("g-3"), Verified("true")),
                ExternalIdentityProvider.Google)
            .Should().BeNull();
    }

    [Fact]
    public void Extract_PopulatesProviderKeyEmailAndDisplayName()
    {
        ExternalAuthClaims? result = ExternalAuthClaimsExtractor.Extract(
            Principal(
                Key("g-4"),
                Email("  user@gmail.com  "),
                ("name", "Ada Lovelace"),
                Verified("true")),
            ExternalIdentityProvider.Google);

        result.Should().NotBeNull();
        result!.ProviderKey.Should().Be("g-4");
        result.Email.Should().Be("user@gmail.com");
        result.DisplayName.Should().Be("Ada Lovelace");
    }
}
