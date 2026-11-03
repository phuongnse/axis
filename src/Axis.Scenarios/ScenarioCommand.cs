using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Scenarios.Runner;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Scenarios;

/// <summary>
/// The scenario runner as a command: it runs every scenario of an application folder, one at a
/// time, each in its own temporary tenant database, and writes the results as JUnit. The scenarios of the
/// modules the application pins run after its own, named <c>&lt;module id&gt;/&lt;name&gt;</c>. The exit code
/// is 0 when every scenario passed, 1 when one failed and 2 when the arguments are wrong or the
/// folder does not compile, which is a setup error and not a failing scenario, and 130 when it is cancelled.
/// With <c>--extensions</c> the application is compiled against the extension packages installed in that
/// folder, and no package code runs. With <c>--modules</c> it is compiled against the modules installed in
/// that folder. Problems with an installed package or module are printed and the run carries on.
/// </summary>
public static class ScenarioCommand
{
    public const int Passed = 0;
    public const int Failed = 1;
    public const int SetupError = 2;
    public const int Cancelled = 130;

    public const string DefaultResultsFolder = "artifacts/test-results";

    private const string Usage = "Usage: Axis.Scenarios <folder> --server <connection string> [--extensions <folder>] [--modules <folder>] [--results <folder>]";

    public static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellationToken)
    {
        if (!TryParse(args, out var folder, out var server, out var extensions, out var modules, out var results))
        {
            await output.WriteLineAsync(Usage);
            return SetupError;
        }

        PackageCatalog catalog;
        try
        {
            catalog = PackageCatalog.Load(extensions);
        }
        catch (InvalidOperationException exception)
        {
            await output.WriteLineAsync(exception.Message);
            return SetupError;
        }

        ModuleCatalog moduleCatalog;
        try
        {
            moduleCatalog = ModuleCatalog.Load(modules);
        }
        catch (InvalidOperationException exception)
        {
            await output.WriteLineAsync(exception.Message);
            return SetupError;
        }

        var compiled = ApplicationCompiler.Compile(folder, catalog.Versions, moduleCatalog.Modules);
        foreach (var diagnostic in catalog.Diagnostics.Concat(moduleCatalog.Diagnostics).Concat(compiled.Diagnostics))
        {
            await output.WriteLineAsync($"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (compiled.HasErrors || compiled.Model is null)
        {
            await output.WriteLineAsync($"The application folder '{folder}' does not compile. No scenario ran.");
            return SetupError;
        }

        var application = compiled.Model.Manifest.Name;
        var scenarios = ApplicationLoader.Load(folder).Scenarios.OrderBy(scenario => scenario.File, StringComparer.Ordinal).Concat(compiled.ModuleScenarios).ToList();
        var outcomes = new List<ScenarioResult>();
        try
        {
            foreach (var scenario in scenarios)
            {
                var outcome = await ScenarioRunner.RunAsync(folder, scenario, server, NullLoggerFactory.Instance, cancellationToken, packages: catalog, modules: moduleCatalog);
                outcomes.Add(outcome);
                await output.WriteLineAsync(outcome.Failure is { } failure ? $"FAIL {outcome.Name}: {failure}" : $"PASS {outcome.Name}");
            }
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("Cancelled. The running scenario's database was dropped.");
            return Cancelled;
        }

        await JUnitWriter.WriteAsync(Path.Combine(results, $"scenarios-{application}.xml"), application, folder, outcomes, cancellationToken);
        var failed = outcomes.Count(outcome => !outcome.Passed);
        await output.WriteLineAsync($"{outcomes.Count} scenario(s), {failed} failed.");
        return failed == 0 ? Passed : Failed;
    }

    private static bool TryParse(string[] args, out string folder, out string server, out string? extensions, out string? modules, out string results)
    {
        folder = server = "";
        extensions = null;
        modules = null;
        results = DefaultResultsFolder;
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        folder = args[0];
        string? serverValue = null;
        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                return false;
            }

            switch (args[index])
            {
                case "--server":
                    serverValue = args[index + 1];
                    break;
                case "--extensions":
                    extensions = args[index + 1];
                    break;
                case "--modules":
                    modules = args[index + 1];
                    break;
                case "--results":
                    results = args[index + 1];
                    break;
                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(serverValue))
        {
            return false;
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(serverValue);
        }
        catch (ArgumentException)
        {
            return false;
        }

        server = serverValue;
        return true;
    }
}
