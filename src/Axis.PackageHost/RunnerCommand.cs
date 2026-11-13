namespace Axis.PackageHost;

/// <summary>The command that starts a runner: the file to run and the arguments that come before <c>runner</c>.</summary>
internal sealed record RunnerCommand(string FileName, IReadOnlyList<string> Arguments)
{
    /// <summary>
    /// The command that starts this program again. Under the <c>dotnet</c> muxer the program's own assembly
    /// is the first argument.
    /// </summary>
    public static RunnerCommand ForThisProgram()
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("The path of this program is not known.");
        return Path.GetFileNameWithoutExtension(path) == "dotnet"
            ? new RunnerCommand(path, [typeof(PackageHostProgram).Assembly.Location])
            : new RunnerCommand(path, []);
    }
}
