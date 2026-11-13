using Microsoft.Extensions.Configuration;

namespace Axis.PackageHost;

/// <summary>
/// Installs the dependencies that <c>requirements.txt</c> locks into a package folder with
/// <c>pip install --require-hashes --no-deps</c>, see docs/reference/extension-packages.md#building-a-package.
/// The setting <c>PackageBuild:Python</c> names the Python command that runs pip. pip's own output is never
/// printed, because it holds absolute paths.
/// </summary>
internal static class PipInstaller
{
    public const string CommandSetting = "PackageBuild:Python";

    private const string DefaultCommand = "python3";

    /// <summary>The outcome of an install: the reason pip refused the dependencies, or the reason it did not complete.</summary>
    public sealed record InstallResult(string? Refusal, string? Failure);

    /// <summary>Installs into <paramref name="folder"/> itself, so the modules sit next to the adapter.</summary>
    public static async Task<InstallResult> InstallAsync(IConfiguration configuration, string folder, CancellationToken cancellationToken)
    {
        var command = configuration[CommandSetting] is { Length: > 0 } configured ? configured : DefaultCommand;
        var result = await ExternalCommand.RunAsync(
            command,
            ["-m", "pip", "install", "--require-hashes", "--no-deps", "--disable-pip-version-check", "--no-input", "--target", folder, "-r", "requirements.txt"],
            workingDirectory: folder,
            cancellationToken);
        if (result is null)
        {
            return new InstallResult(null, $"pip could not start. Set '{CommandSetting}' to the Python command.");
        }

        if (result.ExitCode == 0)
        {
            return new InstallResult(null, null);
        }

        if (result.Output.Contains("DO NOT MATCH THE HASHES", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("DO NOT MATCH THE HASHES", StringComparison.OrdinalIgnoreCase))
        {
            return new InstallResult("A dependency's hash differs from 'requirements.txt', so pip refused it.", null);
        }

        return new InstallResult($"pip could not install the dependencies that 'requirements.txt' locks: it exited with code {result.ExitCode}.", null);
    }
}
