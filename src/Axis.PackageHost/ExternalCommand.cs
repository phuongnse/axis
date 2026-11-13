using System.ComponentModel;
using System.Diagnostics;

namespace Axis.PackageHost;

/// <summary>
/// Runs an external command of the package build, such as Trivy or pip, see
/// docs/reference/extension-packages.md#building-a-package. A cancelled run kills the whole process tree.
/// </summary>
internal static class ExternalCommand
{
    /// <summary>The exit code and the text that the command wrote to its standard output and error.</summary>
    public sealed record Result(int ExitCode, string Output, string Error);

    /// <summary>Runs the command and waits for it. Returns <c>null</c> when it cannot start, because the exception message would name the command.</summary>
    public static async Task<Result?> RunAsync(string command, IEnumerable<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
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

            return new Result(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }
}
