namespace Axis.PackageHost;

/// <summary>
/// Installs the dependencies of a Node.js package from its lock file, see
/// docs/reference/extension-packages.md#building-a-package. It copies the source to a temporary folder and runs
/// <c>npm ci --ignore-scripts</c> there, so the source never changes and no install script of a dependency runs.
/// The caller deletes the folder with <see cref="Delete"/>.
/// </summary>
internal static class NodeInstall
{
    private const string Command = "npm";

    /// <summary>The outcome of an install: the installed folder, or the reason the build stops.</summary>
    public sealed record InstallResult(string? Folder, string? Failure, bool SetupError);

    public static async Task<InstallResult> InstallAsync(string source, string staging, CancellationToken cancellationToken)
    {
        try
        {
            PackageBuildCommand.Copy(source, staging, skipTopLevel: "node_modules");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return new InstallResult(null, "The source could not be copied for the install.", true);
        }

        var result = await ToolProcess.RunAsync(
            Command,
            ["ci", "--ignore-scripts", "--no-audit", "--no-fund", "--no-update-notifier"],
            staging,
            cancellationToken);
        if (!result.Started)
        {
            return new InstallResult(null, "The install could not start. Put npm on the PATH.", true);
        }

        if (result.ExitCode != 0)
        {
            // npm names the temporary folder in its messages, and it is never passed on.
            var error = result.Error.Replace(staging, ".", StringComparison.Ordinal).TrimEnd();
            return new InstallResult(null, $"npm ci failed with exit code {result.ExitCode}.{Environment.NewLine}{error}", false);
        }

        return new InstallResult(staging, null, false);
    }

    /// <summary>A new folder name under the system temporary folder, which is not created.</summary>
    public static string NewStagingFolder() => Path.Combine(Path.GetTempPath(), $"axis-package-build-{Guid.NewGuid():N}");

    public static void Delete(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temporary folder that stays is left to the system to clean.
        }
    }
}
