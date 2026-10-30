using Xunit.Sdk;

namespace Axis.Expressions.Tests;

public sealed class FuzzTests
{
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(30);

    [Fact]
    public void A_check_that_throws_fails_the_run_naming_the_seed_and_the_input()
    {
        var failure = Assert.ThrowsAny<XunitException>(() => Fuzz.Run(
            random => $"a\"{random.Next(10)}\\\n",
            _ => throw new InvalidOperationException("boom"),
            seed: 77,
            budget: _budget));

        Assert.Contains("seed 77", failure.Message, StringComparison.Ordinal);
        Assert.Contains("AXIS_FUZZ_SEED=77", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"input 1 ", failure.Message, StringComparison.Ordinal);
        Assert.Contains(Fuzz.Literal($"a\"{new Random(77).Next(10)}\\\n"), failure.Message, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", failure.Message, StringComparison.Ordinal);
        Assert.Contains("boom", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_check_that_runs_past_the_per_input_limit_fails_the_run_naming_the_seed_and_the_input()
    {
        var failure = Assert.ThrowsAny<XunitException>(() => Fuzz.Run(
            _ => "slow",
            _ => Thread.Sleep(TimeSpan.FromSeconds(5)),
            seed: 5,
            budget: _budget,
            perInput: TimeSpan.FromMilliseconds(50)));

        Assert.Contains("seed 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains("\"slow\"", failure.Message, StringComparison.Ordinal);
        Assert.Contains("time limit of 50 ms", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_message_shows_the_input_with_the_given_writer()
    {
        var failure = Assert.ThrowsAny<XunitException>(() => Fuzz.Run(
            random => random.Next(1000),
            _ => throw new InvalidOperationException(),
            input => $"number {input}",
            seed: 1,
            budget: _budget));

        Assert.Contains($"number {new Random(1).Next(1000)}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_seed_gives_the_same_inputs_in_the_same_order()
    {
        List<int> First() => Draw(seed: 123);

        Assert.Equal(First(), First());
        Assert.NotEqual(First(), Draw(seed: 124));
    }

    [Fact]
    public void A_run_stops_after_the_maximum_number_of_inputs()
    {
        var count = 0;

        Fuzz.Run(random => random.Next(), _ => Interlocked.Increment(ref count), seed: 1, budget: _budget, maxInputs: 25);

        Assert.Equal(25, count);
    }

    [Fact]
    public void A_run_stops_when_the_budget_is_spent()
    {
        var count = 0;

        Fuzz.Run(random => random.Next(), _ => Interlocked.Increment(ref count), seed: 1, budget: TimeSpan.FromMilliseconds(20), maxInputs: int.MaxValue);

        Assert.InRange(count, 1, int.MaxValue - 1);
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a\"b\\c", "\"a\\\"b\\\\c\"")]
    [InlineData("line\n\u0000", "\"line\\u000A\\u0000\"")]
    [InlineData("é", "\"\\u00E9\"")]
    public void A_literal_escapes_quotes_backslashes_and_chars_outside_printable_ascii(string text, string expected) =>
        Assert.Equal(expected, Fuzz.Literal(text));

    [Fact]
    public void A_literal_escapes_a_lone_surrogate() =>
        Assert.Equal("\"\\uD83D\"", Fuzz.Literal(string.Concat('\ud83d')));

    private static List<int> Draw(int seed)
    {
        var inputs = new List<int>();
        Fuzz.Run(random => random.Next(), inputs.Add, seed: seed, budget: _budget, maxInputs: 10);
        return inputs;
    }
}
