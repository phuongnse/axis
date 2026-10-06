namespace Axis.Configuration.Releases;

/// <summary>What another application already holds when an active release cannot be set.</summary>
public enum ActiveReleaseConflict
{
    None,
    Name,
    SitePath,
}

/// <summary>
/// The outcome of setting an active release. <see cref="SitePath"/> is the taken path when
/// <see cref="Conflict"/> is <see cref="ActiveReleaseConflict.SitePath"/>.
/// </summary>
public sealed record SetActiveReleaseResult(ActiveReleaseConflict Conflict, string? SitePath = null)
{
    public static readonly SetActiveReleaseResult Set = new(ActiveReleaseConflict.None);

    public bool IsSet => Conflict == ActiveReleaseConflict.None;
}
