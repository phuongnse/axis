namespace Axis.Configuration.Storage;

/// <summary>
/// The fingerprint of the computed fields of one entity in a release; see
/// <see cref="Releases.ComputedFingerprints"/>. A release has one for each entity that is not a
/// child entity. A release stored before fingerprints existed has none.
/// </summary>
public sealed class ReleaseComputedFingerprint
{
    public required Guid ReleaseId { get; init; }

    public required Guid EntityId { get; init; }

    /// <summary>The lowercase hex SHA-256.</summary>
    public required string Fingerprint { get; init; }
}
