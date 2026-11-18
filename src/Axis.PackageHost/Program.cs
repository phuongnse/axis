using System.Runtime.InteropServices;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost;

/// <summary>
/// The command line entry point. A named class keeps it from clashing with other projects' <c>Program</c>.
/// <c>host</c> listens for runner protocol connections and starts a runner for each, <c>runner</c> runs one
/// installed package version, <c>connect</c> runs the installed packages as a customer-hosted runner, and any other arguments are the
/// <c>build</c> command.
/// </summary>
internal static class PackageHostProgram
{
    private static async Task<int> Main(string[] args)
    {
        switch (args)
        {
            case ["host", ..]:
                return await RunHostAsync(args[1..]);
            case ["connect", ..]:
                return await ConnectAsync(args[1..]);
            case ["runner", var root, var id, var version]:
                return await Runner.RunAsync(root, id, version);
            default:
                return await BuildAsync(args);
        }
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return await PackageBuildCommand.RunAsync(args, configuration, Console.Out, cancellation.Token);
    }

    /// <summary>
    /// Runs the <c>connect</c> command until Ctrl+C or SIGTERM, with the settings of <c>Connect:*</c> and <c>Extensions:Path</c>, see
    /// docs/reference/extension-packages.md#customer-hosted-runners.
    /// </summary>
    private static async Task<int> ConnectAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ConnectCommand).FullName!);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancellation.Cancel();
        });

        return await ConnectCommand.RunAsync(
            builder.Configuration, builder.Environment.IsDevelopment(), RunnerCommands.ForThisProgram(), Console.Error, logger, cancellation.Token);
    }

    /// <summary>
    /// Runs host mode until it is stopped, on the port and address of <c>PackageHost:Port</c> and <c>PackageHost:Address</c>, see
    /// docs/reference/extension-packages.md#the-runner-protocol. It does not start when <c>Extensions:Path</c> is missing or names no
    /// folder, or when either of the other two settings is malformed.
    /// </summary>
    private static async Task<int> RunHostAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var setting = builder.Configuration[PackageCatalog.PathSetting];
        if (string.IsNullOrEmpty(setting))
        {
            await Console.Error.WriteLineAsync($"Set '{PackageCatalog.PathSetting}' to an existing folder.");
            return PackageBuildCommand.SetupError;
        }

        var extensions = Path.GetFullPath(setting, builder.Environment.ContentRootPath);
        if (!Directory.Exists(extensions))
        {
            await Console.Error.WriteLineAsync($"The folder set by '{PackageCatalog.PathSetting}' does not exist.");
            return PackageBuildCommand.SetupError;
        }

        if (!RunnerHostService.TryReadEndpoint(builder.Configuration, out var endpoint, out var error))
        {
            await Console.Error.WriteLineAsync(error);
            return PackageBuildCommand.SetupError;
        }

        builder.Services.AddSingleton(provider => new RunnerHost(extensions, RunnerCommands.ForThisProgram(), provider.GetRequiredService<ILogger<RunnerHost>>(), Sandbox.ForThisMachine(), builder.Configuration));
        builder.Services.AddHostedService(provider => new RunnerHostService(provider.GetRequiredService<RunnerHost>(), endpoint));
        await builder.Build().RunAsync();
        return 0;
    }
}
