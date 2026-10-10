using Axis.Data.Records;

namespace Axis.Data.Tests;

public sealed class SequenceFormatTests
{
    [Theory]
    [InlineData("T-{yyyy}-{n:4}", 2026, 7, "T-2026-0007")]
    [InlineData("PR-{yyyy}-{n:5}", 2026, 42, "PR-2026-00042")]
    [InlineData("N{n}", 2026, 7, "N7")]
    [InlineData("{n:1}", 2026, 0, "0")]
    [InlineData("T-{n:4}", 2026, 123456, "T-123456")]
    [InlineData("{yyyy}/{n:2}/{yyyy}", 2027, 3, "2027/03/2027")]
    [InlineData("{yyyy}-{n:3}", 999, 5, "0999-005")]
    public void Format_replaces_the_year_and_the_padded_number(string format, int year, long number, string expected) =>
        Assert.Equal(expected, SequenceFormat.Format(format, year, number));

    [Theory]
    [InlineData("T-{yyyy}-{n:4}", 2026)]
    [InlineData("{n}-{yyyy}-{yyyy}", 2026)]
    [InlineData("T-{n:4}", 0)]
    [InlineData("{n}", 0)]
    public void Period_is_the_year_only_when_the_format_holds_it(string format, int expected) =>
        Assert.Equal(expected, SequenceFormat.Period(format, 2026));
}
