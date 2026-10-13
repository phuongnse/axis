using Axis.Configuration.Packages;

namespace Axis.Configuration.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("1.2.0", "1.2.0", true)]
    [InlineData("1.2.0", "1.2.1", false)]
    [InlineData("^1.2.0", "1.2.0", true)]
    [InlineData("^1.2.0", "1.10.0", true)]
    [InlineData("^1.2.0", "1.1.9", false)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("~1.2.0", "1.2.9", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    [InlineData("~1.2.3", "1.2.2", false)]
    [InlineData("^0.2.0", "0.9.0", true)]
    [InlineData("^0.2.0", "0.1.9", false)]
    [InlineData("^0.2.0", "1.0.0", false)]
    [InlineData("^9.0.0", "10.0.0", false)]
    [InlineData("~1.9.0", "1.10.0", false)]
    public void A_range_matches_only_the_versions_it_allows(string text, string version, bool matches)
    {
        Assert.True(VersionRange.TryParse(text, out var range));
        Assert.Equal(matches, range.Matches(version));
    }

    [Theory]
    [InlineData("01.0.0")]
    [InlineData("^1.02.0")]
    [InlineData("~1.0.00")]
    [InlineData("1.x")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("=1.0.0")]
    [InlineData("^^1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("")]
    public void Leading_zeros_and_other_forms_are_invalid(string text)
    {
        Assert.False(VersionRange.TryParse(text, out var range));
        Assert.Null(range);
    }

    [Fact]
    public void A_version_with_leading_zeros_never_matches()
    {
        Assert.True(VersionRange.TryParse("^1.0.0", out var range));
        Assert.False(range.Matches("1.01.0"));
    }
}
