using Axis.Rules.Domain;
using FluentAssertions;

namespace Axis.Rules.Domain.Tests;

public sealed class RuleValueTests
{
    [Fact]
    public void Create_WhenDateTimeHasOffset_CanonicalizesToUtc()
    {
        RuleValue value = RuleValue.Create(
            RuleValueType.DateTime,
            ["2026-07-10T10:30:00+07:00"]).Value;

        value.Values.Should().Equal("2026-07-10T03:30:00.0000000+00:00");
    }

    [Fact]
    public void Create_WhenDateTimeHasNoOffset_ReturnsFailure()
    {
        RuleValue.Create(RuleValueType.DateTime, ["2026-07-10T10:30:00"])
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenMultipleValuesAreNotAllowed_ReturnsFailure()
    {
        RuleValue.Create(RuleValueType.Text, ["one", "two"])
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_WhenDecimalUsesInvariantInput_CanonicalizesValue()
    {
        RuleValue.Create(RuleValueType.Decimal, ["001.500"])
            .Value.Values.Should().Equal("1.500");
    }
}
