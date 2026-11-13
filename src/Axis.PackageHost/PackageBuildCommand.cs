using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// The <c>build</c> command: it checks a published C# package folder or a Node.js or Python package source and writes
/// it to <c>&lt;Extensions:Path&gt;/&lt;id&gt;/&lt;version&gt;/</c>, see docs/reference/extension-packages.md#building-a-package.
/// The checks run in a fixed order and the first refusal stops the build: the manifest, the lock file,
/// an existing target folder, then, for Node.js, the install from its lock file and, for Python, the install of the
/// hashed requirements, each into a temporary copy of the source, then the dependency scan. Only then is the package
/// written and the extensions folder loaded as the hosts load it, and a version that does not load is deleted again.
/// The scan and the write read the installed copy, which is deleted afterwards. The exit code is 0 when the package
/// is built, 1 when it is refused, including an install that fails, and 2 for a setup error: a wrong argument, a
/// missing extensions folder, an install that cannot start or a scan that did not complete.
/// </summary>
public static class PackageBuildCommand
{
    public const int Built = 0;
    public const int Refused = 1;
    public const int SetupError = 2;

    private const string Usage = "Usage: Axis.PackageHost build <source>";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, CancellationToken cancellationToken)
    {
        if (args is not ["build", var source])
        {
            await output.WriteLineAsync(Usage);
            return SetupError;
        }

        var extensions = configuration[PackageCatalog.PathSetting];
        if (string.IsNullOrEmpty(extensions) || !Directory.Exists(extensions))
        {
            await output.WriteLineAsync($"Set '{PackageCatalog.PathSetting}' to an existing folder.");
            return SetupError;
        }

        if (!Directory.Exists(source))
        {
            await output.WriteLineAsync("The source folder does not exist.");
            return SetupError;
        }

        var check = PackageCatalog.CheckFolder(source);
        foreach (var diagnostic in check.Diagnostics)
        {
            await output.WriteLineAsync($"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}");
        }

        if (check.Package is not { } package || check.Diagnostics.Count > 0)
        {
            return Refused;
        }

        var manifest = package.Manifest;
        var lockProblems = manifest.Language == PackageLanguage.Python
            ? RequirementsCheck.Check(source, manifest)
            : PackageLockCheck.Check(source, manifest);
        foreach (var problem in lockProblems)
        {
            await output.WriteLineAsync(problem);
        }

        if (lockProblems.Count > 0)
        {
            return Refused;
        }

        var relative = $"{manifest.Id}/{manifest.Version}";
        var target = Path.Combine(extensions, manifest.Id, manifest.Version);
        if (Directory.Exists(target))
        {
            await output.WriteLineAsync($"The folder '{relative}' already exists. Build a new version, or remove the folder first.");
            return Refused;
        }

        var built = source;
        try
        {
            if (manifest.Language == PackageLanguage.Node)
            {
                built = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
                var install = await NodeInstall.InstallAsync(source, built, cancellationToken);
                if (install.Failure is not null)
                {
                    await output.WriteLineAsync(install.Failure);
                    return install.SetupError ? SetupError : Refused;
                }
            }
            else if (manifest.Language == PackageLanguage.Python)
            {
                // The source may be mounted read-only, and a copy outside the extensions folder keeps a half-installed
                // folder out of reach of a host that starts during the build.
                built = Directory.CreateTempSubdirectory("axis-package-build-").FullName;
                Copy(source, built);
                var install = await PipInstaller.InstallAsync(configuration, built, cancellationToken);
                if (install.Failure is not null)
                {
                    await output.WriteLineAsync(install.Failure);
                    return SetupError;
                }

                if (install.Refusal is not null)
                {
                    await output.WriteLineAsync(install.Refusal);
                    return Refused;
                }
            }

            return await ScanAndWriteAsync(configuration, built, extensions, manifest, relative, target, output, cancellationToken);
        }
        finally
        {
            if (built != source)
            {
                try
                {
                    Directory.Delete(built, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A temporary folder that stays is left to the operating system.
                }
            }
        }
    }

    /// <summary>The scan, the write and the final load of the package in <paramref name="built"/>.</summary>
    private static async Task<int> ScanAndWriteAsync(
        IConfiguration configuration,
        string built,
        string extensions,
        PackageManifest manifest,
        string relative,
        string target,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var scan = await TrivyScanner.ScanAsync(configuration, built, cancellationToken);
        if (scan.Failure is not null)
        {
            await output.WriteLineAsync(scan.Failure);
            return SetupError;
        }

        foreach (var finding in scan.Findings)
        {
            await output.WriteLineAsync(finding);
        }

        if (scan.Findings.Count > 0)
        {
            return Refused;
        }

        try
        {
            Copy(built, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            Remove(extensions, manifest.Id, target);
            await output.WriteLineAsync($"The folder '{relative}' could not be written.");
            return SetupError;
        }

        PackageCatalog catalog;
        try
        {
            catalog = PackageCatalog.Load(extensions);
        }
        catch (InvalidOperationException exception)
        {
            Remove(extensions, manifest.Id, target);
            await output.WriteLineAsync(exception.Message);
            return SetupError;
        }

        var rejected = catalog.Diagnostics.Where(diagnostic => diagnostic.File.StartsWith($"{relative}/", StringComparison.Ordinal)).ToList();
        if (rejected.Count > 0 || catalog.Find(manifest.Id, manifest.Version) is null)
        {
            foreach (var diagnostic in rejected)
            {
                await output.WriteLineAsync($"{diagnostic.Code} {diagnostic.File} {diagnostic.Path}: {diagnostic.Message}");
            }

            Remove(extensions, manifest.Id, target);
            await output.WriteLineAsync($"The hosts would not load '{relative}', so it was removed.");
            return Refused;
        }

        await output.WriteLineAsync($"Built {manifest.Id} {manifest.Version} in {relative}/.");
        return Built;
    }

    /// <summary>Copies a folder with its files and subfolders, without the top-level folder <paramref name="skipTopLevel"/> when it is given.</summary>
    internal static void Copy(string source, string target, string? skipTopLevel = null)
    {
        Directory.CreateDirectory(target);
        var skipped = skipTopLevel is null ? null : Path.Combine(source, skipTopLevel) + Path.DirectorySeparatorChar;
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            if (skipped is not null && file.StartsWith(skipped, StringComparison.Ordinal))
            {
                continue;
            }

            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    /// <summary>Deletes the version folder just written, and the id folder when that leaves it empty.</summary>
    private static void Remove(string extensions, string id, string target)
    {
        try
        {
            Directory.Delete(target, recursive: true);
            var idFolder = Path.Combine(extensions, id);
            if (!Directory.EnumerateFileSystemEntries(idFolder).Any())
            {
                Directory.Delete(idFolder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder stays and the next build refuses it as existing.
        }
    }
}
