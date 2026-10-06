namespace Axis.Server.Applications;

/// <summary>Application folders to compile and activate in every tenant when the server starts, as absolute paths.</summary>
internal sealed record StartupActivationOptions(IReadOnlyList<string> Folders)
{
    public const string SectionName = "ActivateOnStartup";
}
