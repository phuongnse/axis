using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// The syntax check of a Python package's build, see docs/reference/extension-packages.md#building-a-package. It byte-compiles each
/// <c>.py</c> file of a folder with Python's <c>compile()</c>, so no package code runs. The script is embedded in this assembly. It runs
/// with the <c>PackageBuild:Python</c> command, <c>-I -B</c>, an empty environment and the folder as its working folder, so it writes no
/// <c>.pyc</c> file, and it prints one JSON line with its report.
/// </summary>
internal static class PythonSyntaxCheck
{
    private const string DefaultCommand = "python3";

    private static readonly string _script = LoadScript();

    /// <summary>
    /// What the check came to: the number of files that <paramref name="Compiled"/> and the <paramref name="Problems"/>, one for each file
    /// that does not, or the <paramref name="SetupError"/> that stopped the check, which is the operator's problem and not the author's.
    /// </summary>
    public sealed record Result(int Compiled, IReadOnlyList<PackageChecks.Problem> Problems, string? SetupError);

    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async Task<Result> CheckAsync(IConfiguration configuration, string folder, CancellationToken cancellationToken)
    {
        var command = configuration[PipInstaller.CommandSetting] is { Length: > 0 } configured ? configured : DefaultCommand;
        var result = await ExternalCommand.RunAsync(command, ["-I", "-B", "-c", _script, folder], workingDirectory: folder, cancellationToken, emptyEnvironment: true);
        if (result is null)
        {
            return SetupFailure($"The Python command could not start. Set '{PipInstaller.CommandSetting}' to the Python command.");
        }

        return Parse(result.Output) ?? SetupFailure($"The Python syntax check did not report. Set '{PipInstaller.CommandSetting}' to the Python command.");
    }

    private static Result SetupFailure(string message) => new(0, [], message);

    /// <summary>The report in the last line of the output, or <see langword="null"/> when that line does not hold one.</summary>
    private static Result? Parse(string output)
    {
        var report = output.Split('\n').LastOrDefault(text => text.Trim().Length > 0);
        if (report is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(report);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("compiled", out var compiled)
                || !document.RootElement.TryGetProperty("problems", out var problems)
                || problems.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var list = new List<PackageChecks.Problem>();
            foreach (var problem in problems.EnumerateArray())
            {
                var file = problem.GetProperty("file").GetString();
                var type = problem.GetProperty("type").GetString();
                var message = problem.GetProperty("message").GetString();
                if (file is null || type is null || message is null)
                {
                    return null;
                }

                if (Number(problem.GetProperty("line")) is not { } line)
                {
                    list.Add(new PackageChecks.Problem(file, PackageTests.Cut($"{file}: {type}: {message}")));
                    continue;
                }

                var column = Number(problem.GetProperty("column")) ?? 1;
                list.Add(new PackageChecks.Problem(file, PackageTests.Cut($"{file}:{line}:{column}: {type}: {message}"), line, column));
            }

            return new Result(compiled.GetInt32(), list, null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The position that <paramref name="element"/> holds, or <see langword="null"/> when it is not a whole number from 1.</summary>
    private static int? Number(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value) && value >= 1 ? value : null;

    private static string LoadScript()
    {
        const string resourceName = "Axis.PackageHost.axis_syntax_check.py";
        using var stream = typeof(PythonSyntaxCheck).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded script '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
