namespace Axis.Configuration.Storage;

/// <summary>
/// One activation of a release for an application: a row of the append-only release history. A release
/// holds one <see cref="Number"/> per application for all of its rows.
/// </summary>
public sealed class ReleaseActivationRow
{
    public required Guid Id { get; init; }

    public required Guid ReleaseId { get; init; }

    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The release number: 1, 2, 3… per application.</summary>
    public required int Number { get; init; }

    public required string ContentHash { get; init; }

    public required DateTimeOffset ActivatedAt { get; init; }

    /// <summary>A user id, <c>startup</c>, <c>operator</c>, or <c>unknown</c> for a row that a migration added.</summary>
    public required string Actor { get; init; }

    public string? ActorName { get; init; }

    public string? Label { get; init; }

    public string? Note { get; init; }

    public Guid? RequestId { get; init; }
}
