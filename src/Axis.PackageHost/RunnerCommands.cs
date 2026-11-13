namespace Axis.PackageHost;

/// <summary>The command that starts the runner of each package language, see docs/reference/extension-packages.md#the-runner-protocol.</summary>
internal sealed record RunnerCommands(RunnerCommand CSharp, RunnerCommand Node, RunnerCommand Python)
{
    public static RunnerCommands ForThisProgram() => new(RunnerCommand.ForThisProgram(), RunnerCommand.ForNode(), RunnerCommand.ForPython());
}
