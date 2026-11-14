using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// The checks of building a package, see docs/reference/extension-packages.md#building-a-package, shared by the
/// <c>build</c> command and the <c>build</c> message of host mode. They run in a fixed order and the first refusal
/// stops them: the manifest, the lock file, an existing target folder when <c>extensions</c> is given, then, for Node.js,
/// the install from its lock file and, for Python, the install of the hashed requirements, each into a temporary copy
/// of the source, then the dependency scan. A C# package is checked in place.
/// </summary>
internal static class PackageChecks
{
    /// <summary>One refusal. <paramref name="Message"/> is the line that the <c>build</c> command prints for it.</summary>
    public sealed record Problem(string File, string Message);

    /// <summary>
    /// The result of the checks: the manifest and the folder that holds the built package, or the problems that refuse it, or
    /// the setup error that stopped the checks. Disposing it deletes the folder when it is a temporary copy.
    /// </summary>
    public sealed class Outcome(string source) : IDisposable
    {
        public PackageManifest? Manifest { get; internal set; }

        /// <summary>The folder with the built package: the source itself, or a temporary copy that <see cref="Dispose"/> deletes.</summary>
        public string? Folder { get; internal set; }

        public IReadOnlyList<Problem> Problems { get; internal set; } = [];

        /// <summary>Why the checks could not run, which is the operator's problem and not the author's.</summary>
        public string? SetupError { get; internal set; }

        public void Dispose()
        {
            if (Folder is null || Folder == source)
            {
                return;
            }

            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temporary folder that stays is left to the operating system.
            }

            Folder = null;
        }
    }

    /// <summary>
    /// Runs the checks on <paramref name="source"/>. <paramref name="extensions"/> is the extensions folder whose
    /// <c>&lt;id&gt;/&lt;version&gt;</c> folder must not exist yet, or <see langword="null"/> for no such check.
    /// <paramref name="mode"/> is how the manifest check reads the assembly of a C# package.
    /// </summary>
    public static async Task<Outcome> RunAsync(IConfiguration configuration, string source, PackageLoadMode mode, string? extensions, CancellationToken cancellationToken)
    {
        var outcome = new Outcome(source);
        try
        {
            await CheckAsync(outcome, configuration, source, mode, extensions, cancellationToken);
        }
        catch
        {
            outcome.Dispose();
            throw;
        }

        return outcome;
    }

    private static async Task CheckAsync(Outcome outcome, IConfiguration configuration, string source, PackageLoadMode mode, string? extensions, CancellationToken cancellationToken)
    {
        var check = PackageCatalog.CheckFolder(source, mode);
        if (check.Package is not { } package || check.Diagnostics.Count > 0)
        {
            outcome.Problems =
            [
                .. check.Diagnostics.Select(diagnostic => new Problem(diagnostic.File, $"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}")),
            ];
            return;
        }

        var manifest = package.Manifest;
        outcome.Manifest = manifest;
        var lockProblems = manifest.Language == PackageLanguage.Python
            ? RequirementsCheck.Check(source, manifest)
            : PackageLockCheck.Check(source, manifest);
        if (lockProblems.Count > 0)
        {
            outcome.Problems = [.. lockProblems.Select(problem => new Problem(manifest.Lock, problem))];
            return;
        }

        var relative = $"{manifest.Id}/{manifest.Version}";
        if (extensions is not null && Directory.Exists(Path.Combine(extensions, manifest.Id, manifest.Version)))
        {
            outcome.Problems = [new Problem(relative, $"The folder '{relative}' already exists. Build a new version, or remove the folder first.")];
            return;
        }

        outcome.Folder = source;
        if (manifest.Language == PackageLanguage.Node)
        {
            outcome.Folder = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
            var install = await NodeInstall.InstallAsync(source, outcome.Folder, cancellationToken);
            if (install.Failure is not null)
            {
                Stop(outcome, manifest, install.Failure, install.SetupError);
                return;
            }
        }
        else if (manifest.Language == PackageLanguage.Python)
        {
            // The source may be mounted read-only, and a copy outside the extensions folder keeps a half-installed
            // folder out of reach of a host that starts during the build.
            outcome.Folder = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
            PackageBuildCommand.Copy(source, outcome.Folder);
            var install = await PipInstaller.InstallAsync(configuration, outcome.Folder, cancellationToken);
            if (install.Failure is not null)
            {
                Stop(outcome, manifest, install.Failure, setupError: true);
                return;
            }

            if (install.Refusal is not null)
            {
                Stop(outcome, manifest, install.Refusal, setupError: false);
                return;
            }
        }

        var scan = await TrivyScanner.ScanAsync(configuration, outcome.Folder, cancellationToken);
        if (scan.Failure is not null)
        {
            Stop(outcome, manifest, scan.Failure, setupError: true);
            return;
        }

        outcome.Problems = [.. scan.Findings.Select(finding => new Problem(manifest.Lock, finding))];
    }

    private static void Stop(Outcome outcome, PackageManifest manifest, string message, bool setupError)
    {
        if (setupError)
        {
            outcome.SetupError = message;
        }
        else
        {
            outcome.Problems = [new Problem(manifest.Lock, message)];
        }
    }
}
