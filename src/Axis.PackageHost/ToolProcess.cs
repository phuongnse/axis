using System.Diagnostics;

namespace Axis.PackageHost;

/// <summary>
/// Runs a command-line tool of the package build, the Trivy scan and the npm install alike, see
/// docs/reference/extension-packages.md#building-a-package. The tool is killed with its child processes when the
/// build is cancelled.
/// </summary>
internal static class ToolProcess
{
    /// <summary>The outcome of a run. <paramref name="Started"/> is false when the command could not start.</summary>
    public sealed record ToolResult(bool Started, int ExitCode, string Output, string Error);

    public static async Task<ToolResult> RunAsync(string command, IEnumerable<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The process did not start.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new ToolResult(true, process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The exception message names the command, so it is never passed on.
            return new ToolResult(false, -1, "", "");
        }
    }
}
