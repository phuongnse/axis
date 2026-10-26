using Axis.Configuration.Compilation;

namespace Axis.Configuration.Tests;

public sealed class TextPlaceholdersTests
{
    private static readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
    {
        ["title"] = "T",
        ["amount"] = "500",
        ["empty"] = string.Empty,
    };

    [Theory]
    [InlineData("Request {title}", "Request T")]
    [InlineData("{title}{amount}{title}", "T500T")]
    [InlineData("Amount {amount}.", "Amount 500.")]
    [InlineData("a{empty}b", "ab")]
    [InlineData("{{title}}", "{title}")]
    [InlineData("{{{title}}}", "{T}")]
    [InlineData("a {{ b }} c", "a { b } c")]
    [InlineData("{missing}", "{missing}")]
    [InlineData("{Title}", "{Title}")]
    [InlineData("", "")]
    [InlineData("no placeholders", "no placeholders")]
    public void Filling_replaces_each_placeholder_and_turns_doubled_braces_into_single_ones(string text, string expected)
    {
        Assert.Equal(expected, TextPlaceholders.Fill(text, _values));
    }

    [Theory]
    [InlineData("{", "{")]
    [InlineData("a}", "a}")]
    [InlineData("{a", "{a")]
    [InlineData("{é}", "{é}")]
    public void Filling_keeps_a_brace_that_is_not_part_of_a_placeholder(string text, string expected)
    {
        Assert.Equal(expected, TextPlaceholders.Fill(text, _values));
    }

    [Fact]
    public void A_value_is_not_read_for_placeholders_again()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "{b}", ["b"] = "x" };

        Assert.Equal("{b}", TextPlaceholders.Fill("{a}", values));
    }
}
