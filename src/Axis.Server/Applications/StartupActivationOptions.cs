namespace Axis.Server.Applications;

/// <summary>Application folders to compile and activate in every tenant when the server starts, as absolute paths.</summary>
/// <param name="Folders">The absolute application folders.</param>
/// <param name="InlineRecomputeLimit">The most rows of one entity that activation recomputes inline. A larger recompute is left to the worker.</param>
internal sealed record StartupActivationOptions(IReadOnlyList<string> Folders, int InlineRecomputeLimit)
{
    public const string SectionName = "ActivateOnStartup";
}
