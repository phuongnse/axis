namespace Axis.Configuration.Releases;

/// <summary>
/// Who activated a release, as the release history records it: <paramref name="Actor"/> is a user id,
/// <c>startup</c> or <c>operator</c>. The name, label, note and request id are null unless a publish
/// request gives them.
/// </summary>
public sealed record ActivationActor(string Actor, string? ActorName, string? Label, string? Note, Guid? RequestId)
{
    /// <summary>The server's startup activation.</summary>
    public static ActivationActor Startup { get; } = new("startup", null, null, null, null);

    /// <summary>The platform operator's <c>axis-admin activate</c>.</summary>
    public static ActivationActor Operator { get; } = new("operator", null, null, null, null);

    /// <summary>A user who publishes or rolls back through a request.</summary>
    public static ActivationActor User(string id, string? name, string? label, string? note, Guid? requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new ActivationActor(id, name, label, note, requestId);
    }
}
