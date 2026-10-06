using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class SitePathsTests
{
    [Theory]
    [InlineData("records", true)]
    [InlineData("Records", true)]
    [InlineData("a-1", true)]
    [InlineData("api", true)]
    [InlineData("", false)]
    [InlineData("1a", false)]
    [InlineData("-a", false)]
    [InlineData("a_b", false)]
    [InlineData("Krecords", false)]
    public void Path_follows_the_ascii_pattern(string path, bool expected) =>
        Assert.Equal(expected, SitePaths.IsValid(path));

    [Fact]
    public void Path_is_at_most_60_characters()
    {
        Assert.True(SitePaths.IsValid(new string('a', 60)));
        Assert.False(SitePaths.IsValid(new string('a', 61)));
    }

    [Fact]
    public void Null_is_not_a_path() => Assert.False(SitePaths.IsValid(null));
}
