using System.Text.Json;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// Runs the <c>unittest</c> tests of a built Python package, see docs/reference/package-runners.md#the-runner-protocol.
/// The runner script is embedded in this assembly. It runs with the <c>PackageBuild:Python</c> command, <c>-I -B</c>, an empty
/// environment and the built copy as its working folder, and it prints one JSON line with the outcome of each test.
/// </summary>
internal static class PackageTests
{
    /// <summary>The longest <c>message</c> of a test outcome, in characters.</summary>
    private const int MaxMessage = 1000;

    private const string DefaultCommand = "python3";

    private static readonly string _script = LoadScript();

    /// <summary>
    /// What the run came to: the <paramref name="Tests"/> that ran and the <paramref name="Output"/> lines they wrote, or the <paramref name="Refusal"/> that the author causes by a test
    /// that runs too long or ends the process without a result, or the <paramref name="SetupError"/> that stopped the run, which is the
    /// operator's problem and not the author's.
    /// </summary>
    public sealed record Run(IReadOnlyList<PackageTestOutcome>? Tests, string? Refusal, string? SetupError, IReadOnlyList<string>? Output = null);

    /// <summary>
    /// Runs the test files, paths relative to <paramref name="folder"/> that use <c>/</c>, and stops them after <paramref name="limit"/>, which
    /// kills their process tree.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="stopping"/> was cancelled.</exception>
    public static async Task<Run> RunAsync(IConfiguration configuration, string folder, IEnumerable<string> testFiles, TimeSpan limit, CancellationToken stopping)
    {
        var paths = testFiles.Order(StringComparer.Ordinal).ToList();
        if (paths.Count == 0)
        {
            return new Run([], null, null);
        }

        var command = configuration[PipInstaller.CommandSetting] is { Length: > 0 } configured ? configured : DefaultCommand;
        string[] arguments = ["-I", "-B", "-c", _script, folder, Path.Combine(AppContext.BaseDirectory, "sdk", "python"), .. paths];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(limit);
        ExternalCommand.Result? result;
        try
        {
            result = await ExternalCommand.RunAsync(command, arguments, workingDirectory: folder, timeout.Token, emptyEnvironment: true);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            return new Run(null, $"The package's tests ran longer than {limit.TotalSeconds:0} seconds, so they were stopped.", null);
        }

        if (result is null)
        {
            return new Run(null, null, $"The Python command could not start. Set '{PipInstaller.CommandSetting}' to the Python command.");
        }

        return Parse(result.Output) is { } tests
            ? new Run(tests, null, null, OutputLines.Of(result.Error))
            : new Run(null, "The package's tests ended without reporting their outcomes.", null);
    }

    /// <summary>The outcomes in the last line of the output, or <see langword="null"/> when that line does not hold them.</summary>
    private static List<PackageTestOutcome>? Parse(string output)
    {
        var line = output.Split('\n').LastOrDefault(text => text.Trim().Length > 0);
        if (line is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tests", out var tests)
                || tests.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var outcomes = new List<PackageTestOutcome>();
            foreach (var test in tests.EnumerateArray())
            {
                if (test.ValueKind != JsonValueKind.Object
                    || test.GetProperty("name").GetString() is not { } name
                    || test.GetProperty("outcome").GetString() is not { } outcome
                    || outcome is not (PackageTestOutcome.Passed or PackageTestOutcome.Failed))
                {
                    return null;
                }

                var message = outcome == PackageTestOutcome.Passed ? null : test.GetProperty("message").GetString();
                if (!test.TryGetProperty("durationMs", out var duration) || !duration.TryGetInt64(out var durationMs) || durationMs < 0)
                {
                    return null;
                }

                string? file = null;
                int? lineNumber = null;
                int? column = null;
                if (outcome == PackageTestOutcome.Failed && test.TryGetProperty("file", out var place))
                {
                    if (place.ValueKind != JsonValueKind.String
                        || !test.TryGetProperty("line", out var lineElement) || !lineElement.TryGetInt32(out var lineValue) || lineValue < 1
                        || !test.TryGetProperty("column", out var columnNumber) || !columnNumber.TryGetInt32(out var columnValue) || columnValue < 1)
                    {
                        return null;
                    }

                    file = place.GetString();
                    lineNumber = lineValue;
                    column = columnValue;
                }

                outcomes.Add(new PackageTestOutcome(name, outcome, message is null ? null : OutputLines.Cut(message, MaxMessage), durationMs, file, lineNumber, column));
            }

            return outcomes;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string LoadScript()
    {
        const string resourceName = "Axis.PackageHost.axis_test_runner.py";
        using var stream = typeof(PackageTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
