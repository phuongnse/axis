namespace Axis.Server.Applications;

/// <summary>Application folders to compile and activate in every tenant when the server starts, as absolute paths.</summary>
/// <param name="Folders">The absolute application folders.</param>
/// <param name="InlineRecomputeLimit">The most rows of one entity that activation recomputes inline. A larger recompute is left to the worker.</param>
/// <param name="ActivationTimeout">The <c>statement_timeout</c> of each statement of an activation transaction. An activation past it rolls back.</param>
/// <param name="ApplicationPackages">Whether <c>Extensions:ApplicationPackages</c> is on. With it off, a folder that holds its own packages does not compile.</param>
internal sealed record StartupActivationOptions(IReadOnlyList<string> Folders, int InlineRecomputeLimit, TimeSpan ActivationTimeout, bool ApplicationPackages)
{
    public const string SectionName = "ActivateOnStartup";
}
