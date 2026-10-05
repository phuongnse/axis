namespace Axis.Configuration.Storage;

/// <summary>
/// A compiled application stored in the tenant database. A release is immutable: it is only ever
/// inserted, and it is unique per application id and content hash.
/// </summary>
public sealed class Release
{
    public required Guid Id { get; init; }

    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The lowercase hex SHA-256 content hash; see <see cref="Releases.ContentHash"/>.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The canonical content of every resource file of the release.</summary>
    public List<ReleaseResource> Resources { get; init; } = [];
}
