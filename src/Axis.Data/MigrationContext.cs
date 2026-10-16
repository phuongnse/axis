using System.Data.Common;
using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// What provisioning needs to apply the pending migrations of a release and to write the active
/// release in the same transaction: the release's id, the entity ids of the application's active
/// release by entity name, and the write itself.
/// </summary>
/// <param name="ReleaseId">The release being activated, recorded as the one that applied the migrations.</param>
/// <param name="StoredEntityIds">
/// The entity ids by name, ignoring letter case, as the application's active release has them. They name the
/// tables that hold the data now, including those of an entity the new release no longer has.
/// </param>
/// <param name="SetActiveReleaseAsync">
/// Writes the active release and its site paths in the given provisioning transaction, and gives the
/// diagnostics of a refused name or site path. It runs only when a migration is pending.
/// </param>
public sealed record MigrationContext(
    Guid ReleaseId,
    IReadOnlyDictionary<string, Guid> StoredEntityIds,
    Func<DbTransaction, CancellationToken, Task<IReadOnlyList<Diagnostic>>> SetActiveReleaseAsync)
{
    /// <summary>No release and no stored entities. Setting the active release throws.</summary>
    public static MigrationContext None { get; } = new(
        Guid.Empty,
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase),
        (_, _) => throw new InvalidOperationException("Provisioning without a migration context cannot set the active release."));
}
