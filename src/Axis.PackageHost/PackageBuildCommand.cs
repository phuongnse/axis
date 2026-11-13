using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// The <c>build</c> command: it checks a C# or Node.js package folder and writes it to
/// <c>&lt;Extensions:Path&gt;/&lt;id&gt;/&lt;version&gt;/</c>, see docs/reference/extension-packages.md#building-a-package.
/// The checks run in a fixed order and the first refusal stops the build: the manifest, the language, the lock file,
/// an existing target folder, the install of a Node.js package, then the dependency scan. Only then is the package
/// written and the extensions folder loaded as the hosts load it, and a version that does not load is deleted again.
/// The scan and the write read the installed folder of a Node.js package, which is a temporary copy of the source
/// that is deleted afterwards. The exit code is 0 when the package is built, 1 when it is refused, including an
/// install that fails, and 2 for a setup error: a wrong argument, a missing extensions folder, an install that
/// cannot start or a scan that did not complete.
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
        if (manifest.Language == PackageLanguage.Python)
        {
            await output.WriteLineAsync($"Only C# and Node.js packages can be built, not '{manifest.Language.ToString().ToLowerInvariant()}'.");
            return Refused;
        }

        var lockProblems = PackageLockCheck.Check(source, manifest);
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

        var staging = manifest.Language == PackageLanguage.Node ? NodeInstall.NewStagingFolder() : null;
        try
        {
            var folder = source;
            if (staging is not null)
            {
                var install = await NodeInstall.InstallAsync(source, staging, cancellationToken);
                if (install.Folder is null)
                {
                    await output.WriteLineAsync(install.Failure);
                    return install.SetupError ? SetupError : Refused;
                }

                folder = install.Folder;
            }

            return await ScanAndWriteAsync(configuration, folder, extensions, manifest, relative, target, output, cancellationToken);
        }
        finally
        {
            if (staging is not null)
            {
                NodeInstall.Delete(staging);
            }
        }
    }

    /// <summary>The scan, the write and the final load, which read the <paramref name="folder"/> that holds the package as it is installed.</summary>
    private static async Task<int> ScanAndWriteAsync(
        IConfiguration configuration, string folder, string extensions, PackageManifest manifest, string relative, string target, TextWriter output, CancellationToken cancellationToken)
    {
        var scan = await TrivyScanner.ScanAsync(configuration, folder, cancellationToken);
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
            Copy(folder, target);
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
