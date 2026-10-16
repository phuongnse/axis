using System.Data.Common;
using Axis.Configuration.Diagnostics;

namespace Axis.Data;

/// <summary>
/// What provisioning needs to apply the pending migrations of a release and to write the active
/// release in the same transaction: the release's id, the entity ids of the application's active
/// release by entity name, and the write itself. Writing the active release under the schema lock
/// keeps another activation from committing a migration between the history check and the write.
/// </summary>
/// <param name="ReleaseId">The release being activated, recorded as the one that applied the migrations.</param>
/// <param name="StoredEntityIds">
/// The entity ids by name, ignoring letter case, as the application's active release has them. They name the
/// tables that hold the data now, including those of an entity the new release no longer has.
/// </param>
/// <param name="SetActiveReleaseAsync">
/// Writes the active release and its site paths in the given provisioning transaction, and gives the
/// diagnostics of a refused name or site path. It runs after the plan is applied, just before the
/// commit. When it is <see langword="null"/>, provisioning does not set the active release.
/// </param>
public sealed record MigrationContext(
    Guid ReleaseId,
    IReadOnlyDictionary<string, Guid> StoredEntityIds,
    Func<DbTransaction, CancellationToken, Task<IReadOnlyList<Diagnostic>>>? SetActiveReleaseAsync)
{
    /// <summary>No release, no stored entities and no active release write.</summary>
    public static MigrationContext None { get; } = new(
        Guid.Empty,
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase),
        null);
}
