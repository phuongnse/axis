using System.ComponentModel;
using System.Diagnostics;
using Axis.Configuration.Packages;

namespace Axis.PackageHost;

/// <summary>
/// Starts and kills the runner process of a package language, see docs/reference/extension-packages.md#the-runner-protocol. Host mode
/// and the <c>connect</c> command start every runner here. The runner's environment is cleared and holds only the variables the caller
/// passes. Its standard input and output carry the protocol.
/// </summary>
internal static class RunnerProcess
{
    /// <summary>
    /// Starts the runner of <paramref name="language"/> for one installed version under <paramref name="root"/>. Returns
    /// <see langword="null"/> when the language has no runner or the runner's program is not installed, so that no runner starts.
    /// </summary>
    public static Process? TryStart(RunnerCommands commands, string root, string package, string version, PackageLanguage language, IReadOnlyDictionary<string, string> environment)
    {
        var command = language switch
        {
            PackageLanguage.CSharp => commands.CSharp,
            PackageLanguage.Node => commands.Node,
            PackageLanguage.Python => commands.Python,
            _ => null,
        };
        if (command is null)
        {
            return null;
        }

        try
        {
            return Start(command, root, package, version, language, environment);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Kills the runner and its child processes and waits until it has exited.</summary>
    public static async Task KillAsync(Process runner)
    {
        try
        {
            runner.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The runner has already exited.
        }

        await runner.WaitForExitAsync();
    }

    private static Process Start(RunnerCommand command, string root, string package, string version, PackageLanguage language, IReadOnlyDictionary<string, string> environment)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in command.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (language == PackageLanguage.Python)
        {
            info.ArgumentList.Add(Path.Combine(root, package, version));
        }
        else
        {
            info.ArgumentList.Add("runner");
            info.ArgumentList.Add(root);
            info.ArgumentList.Add(package);
            info.ArgumentList.Add(version);
        }

        info.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            info.Environment[name] = value;
        }

        return Process.Start(info) ?? throw new InvalidOperationException("The runner did not start.");
    }
}
