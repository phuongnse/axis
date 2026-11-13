namespace Axis.PackageHost;

/// <summary>
/// Installs the dependencies of a Node.js package from its lock file, see
/// docs/reference/extension-packages.md#building-a-package. It copies the source to a temporary folder and runs
/// <c>npm ci --ignore-scripts</c> there, so the source never changes and no install script of a dependency runs.
/// The caller creates the folder and deletes it afterwards.
/// </summary>
internal static class NodeInstall
{
    private const string Command = "npm";

    /// <summary>The outcome of an install: the reason the build stops, or none when <paramref name="staging"/> holds the installed package.</summary>
    public sealed record InstallResult(string? Failure, bool SetupError);

    public static async Task<InstallResult> InstallAsync(string source, string staging, CancellationToken cancellationToken)
    {
        try
        {
            PackageBuildCommand.Copy(source, staging, skipTopLevel: "node_modules");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return new InstallResult("The source could not be copied for the install.", true);
        }

        var result = await ExternalCommand.RunAsync(
            Command,
            ["ci", "--ignore-scripts", "--no-audit", "--no-fund", "--no-update-notifier"],
            workingDirectory: staging,
            cancellationToken);
        if (result is null)
        {
            return new InstallResult("The install could not start. Put npm on the PATH.", true);
        }

        if (result.ExitCode != 0)
        {
            // npm names the temporary folder in its messages, and it is never passed on.
            var error = result.Error.Replace(staging, ".", StringComparison.Ordinal).TrimEnd();
            return new InstallResult($"npm ci failed with exit code {result.ExitCode}.{Environment.NewLine}{error}", false);
        }

        return new InstallResult(null, false);
    }
}
