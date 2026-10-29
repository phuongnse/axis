using System.Globalization;
using System.Text;

namespace Axis.Expressions.Tests;

/// <summary>
/// Runs a fuzz test: a seeded generator feeds inputs to a check until a time budget or an input
/// count runs out. The inputs come from one <see cref="Random"/>, so the same seed gives the same
/// inputs in the same order, whatever the speed of the machine. A check that throws, or that runs
/// past the per-input limit, fails the test with the seed, the input number and the input as a C#
/// string literal, so the input can be pasted into a regular unit test.
/// <c>AXIS_FUZZ_SEED</c> and <c>AXIS_FUZZ_SECONDS</c> override the seed and the budget. The file
/// has no other dependency, so another test project can link it.
/// </summary>
internal static class Fuzz
{
    public const int DefaultSeed = 2025;

    public const string SeedVariable = "AXIS_FUZZ_SEED";

    public const string SecondsVariable = "AXIS_FUZZ_SECONDS";

    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan DefaultPerInput = TimeSpan.FromSeconds(2);

    public const int DefaultMaxInputs = 20_000;

    /// <summary>
    /// Draws inputs from <paramref name="next"/> with one <c>Random(seed)</c> and passes each to
    /// <paramref name="check"/>, until <paramref name="maxInputs"/> or <paramref name="budget"/>.
    /// <paramref name="show"/> writes an input in a failure message and defaults to a C# literal
    /// of its text. The seed defaults to <c>AXIS_FUZZ_SEED</c> or <see cref="DefaultSeed"/> and the
    /// budget to <c>AXIS_FUZZ_SECONDS</c> or <see cref="DefaultBudget"/>. Without <paramref name="maxInputs"/>
    /// the run stops after <see cref="DefaultMaxInputs"/> inputs, unless <c>AXIS_FUZZ_SECONDS</c> is set: then only the budget ends it. A check that hangs leaves
    /// its thread running until the test process ends.
    /// </summary>
    public static void Run<T>(
        Func<Random, T> next,
        Action<T> check,
        Func<T, string>? show = null,
        int? seed = null,
        TimeSpan? budget = null,
        TimeSpan? perInput = null,
        int? maxInputs = null)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(check);

        var actualSeed = seed ?? EnvironmentSeed() ?? DefaultSeed;
        var actualBudget = budget ?? EnvironmentBudget() ?? DefaultBudget;
        var limit = perInput ?? DefaultPerInput;
        var actualMaxInputs = maxInputs ?? (EnvironmentBudget() is null ? DefaultMaxInputs : int.MaxValue);
        show ??= value => Literal(value?.ToString() ?? "");

        var random = new Random(actualSeed);
        var deadline = DateTime.UtcNow + actualBudget;
        for (var index = 1; index <= actualMaxInputs && DateTime.UtcNow < deadline; index++)
        {
            var input = next(random);
            var task = Task.Run(() => check(input));
            bool finished;
            try
            {
                finished = task.Wait(limit);
            }
            catch (AggregateException failure)
            {
                var cause = failure.InnerExceptions.Count == 1 ? failure.InnerExceptions[0] : failure;
                Assert.Fail($"{Describe(actualSeed, index, show(input))} threw:{Environment.NewLine}{cause}");
                return;
            }

            if (!finished)
            {
                Assert.Fail($"{Describe(actualSeed, index, show(input))} ran past the time limit of {limit.TotalMilliseconds:0} ms.");
                return;
            }
        }
    }

    /// <summary>Writes <paramref name="text"/> as a C# string literal: <c>\</c>, <c>"</c> and every char outside printable ASCII are escaped.</summary>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case >= ' ' and <= '~':
                    builder.Append(c);
                    break;
                default:
                    builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                    break;
            }
        }

        return builder.Append('"').ToString();
    }

    private static string Describe(int seed, int index, string input) =>
        $"Fuzz input {index} of seed {seed} (rerun with {SeedVariable}={seed}): {input}";

    private static int? EnvironmentSeed() =>
        int.TryParse(Environment.GetEnvironmentVariable(SeedVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : null;

    private static TimeSpan? EnvironmentBudget() =>
        double.TryParse(Environment.GetEnvironmentVariable(SecondsVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
