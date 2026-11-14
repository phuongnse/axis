using System.Diagnostics;

namespace Axis.Integration.Tests;

/// <summary>The built worker as its own process, to see its exit code and what it prints.</summary>
internal static class WorkerProcess
{
    /// <summary>
    /// Runs the built worker with <paramref name="environment"/>. With <paramref name="stopWhenStarted"/> it sends
    /// SIGTERM once the host reports it started, for a normal shutdown. Otherwise it waits for the
    /// worker to exit on its own.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyDictionary<string, string> environment,
        bool stopWhenStarted,
        CancellationToken cancellationToken)
    {
        var directory = WorkerDirectory();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", "Axis.Worker.dll")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var (key, value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        var output = new System.Text.StringBuilder();
        var started = new TaskCompletionSource();
        void Receive(string? line)
        {
            lock (output)
            {
                output.AppendLine(line);
            }

            if (line is not null && line.Contains("Application started", StringComparison.Ordinal))
            {
                started.TrySetResult();
            }
        }

        process.OutputDataReceived += (_, e) => Receive(e.Data);
        process.ErrorDataReceived += (_, e) => Receive(e.Data);
        Assert.True(process.Start());
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            if (stopWhenStarted)
            {
                await started.Task.WaitAsync(timeout.Token);
                // Give the start check time to run before the shutdown.
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                using var signal = Process.Start("kill", $"-TERM {process.Id}")!;
                await signal.WaitForExitAsync(timeout.Token);
            }

            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            string partial;
            lock (output)
            {
                partial = output.ToString();
            }

            Assert.Fail($"The worker did not finish within 60 seconds. Output:{Environment.NewLine}{partial}");
        }

        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    /// <summary>
    /// The folder with the built worker. The worker project's own build output is complete. The
    /// test output holds a copy that lacks the shared ASP.NET Core assemblies the test host
    /// brings, so it is used only when the project output cannot be found.
    /// </summary>
    private static string WorkerDirectory()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Axis.slnx")))
        {
            repository = repository.Parent;
        }

        if (repository is not null)
        {
            var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var own = Path.Combine(repository.FullName, "src", "Axis.Worker", "bin", framework.Parent!.Name, framework.Name);
            if (File.Exists(Path.Combine(own, "Axis.Worker.runtimeconfig.json")))
            {
                return own;
            }
        }

        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "Axis.Worker.runtimeconfig.json")),
            "The built worker was not found in the worker project's output or the test output.");
        return AppContext.BaseDirectory;
    }
}
