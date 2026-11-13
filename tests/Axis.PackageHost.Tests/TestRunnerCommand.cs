namespace Axis.PackageHost.Tests;

/// <summary>
/// The commands that start a runner of the package host that sits in the test output, under the same <c>dotnet</c> as the test,
/// and the Node.js runner that the test project copies beside it.
/// </summary>
internal static class TestRunnerCommand
{
    public static RunnerCommands Create()
    {
        var assembly = Path.Combine(AppContext.BaseDirectory, "Axis.PackageHost.dll");
        var muxer = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
        var runtimeConfig = Path.Combine(AppContext.BaseDirectory, "Axis.PackageHost.runtimeconfig.json");
        string[] arguments = File.Exists(runtimeConfig) ? ["exec", assembly] : ["exec", "--runtimeconfig", runtimeConfig, assembly];
        return new RunnerCommands(new RunnerCommand(muxer, arguments), RunnerCommand.ForNode());
    }
}
