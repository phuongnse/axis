using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class TeamAccountSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("my-team-account-123")]
    [InlineData("a")]
    public void TeamAccountSlug_WhenValueIsValid_IsCreatedSuccessfully(string value)
    {
        Result<TeamAccountSlug> result = TeamAccountSlug.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    [InlineData("-starts-with-dash")]
    [InlineData("ends-with-dash-")]
    public void TeamAccountSlug_WhenValueIsInvalid_ReturnsFailure(string value)
    {
        Result<TeamAccountSlug> result = TeamAccountSlug.Create(value);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TeamAccountSlug_WhenExceedsMaxLength_ReturnsFailure()
    {
        string tooLong = new string('a', 64);
        Result<TeamAccountSlug> result = TeamAccountSlug.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("63");
    }

    [Fact]
    public void TeamAccountSlug_WhenValuesAreIdentical_AreEqual()
    {
        TeamAccountSlug a = TeamAccountSlug.Create("acme-corp").Value;
        TeamAccountSlug b = TeamAccountSlug.Create("acme-corp").Value;

        a.Should().Be(b);
    }
}
