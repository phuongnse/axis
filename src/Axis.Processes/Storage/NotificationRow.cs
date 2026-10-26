namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.notifications</c>: one in-app notification for one user or one role, which a
/// <c>notify</c> step stored. All the rows of one step run share the notification's id. Times use the
/// database clock.
/// </summary>
public sealed class NotificationRow
{
    /// <summary>The notification's id, a version 7 UUID. Every recipient's row has it.</summary>
    public required Guid Id { get; init; }

    public required Guid ApplicationId { get; init; }

    /// <summary>The release the notifying instance is pinned to. It resolves the text keys and the page.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The kind of the recipient: <c>user</c> or <c>role</c>.</summary>
    public required string RecipientKind { get; init; }

    /// <summary>The user id or the role name.</summary>
    public required string Recipient { get; init; }

    public required string SubjectKey { get; init; }

    public required string BodyKey { get; init; }

    /// <summary>The raw evaluated placeholder values as JSON text, keyed by the placeholders' names. They are formatted when read.</summary>
    public required string Values { get; init; }

    /// <summary>The declared name of the page the notification opens, or null.</summary>
    public string? OpenPage { get; init; }

    /// <summary>The raw evaluated page parameters as JSON text, keyed by the parameters' declared names, or null when there is no page.</summary>
    public string? OpenParameters { get; init; }

    /// <summary>The start of the storing step's transaction.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
