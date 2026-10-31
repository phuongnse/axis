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
/// <param name="ReadStoredEntityIdsAsync">
/// Reads the entity ids by name, ignoring letter case, as the application's active release has them. They name
/// the tables that hold the data now, including those of an entity the new release no longer has. Provisioning
/// calls it only when a migration is pending, and only after it took the schema lock, so it never sees a release
/// that another activation replaces while this one waits for the lock.
/// </param>
/// <param name="ReadOldReleaseWorkAsync">
/// Reads, in the given provisioning transaction, the other releases of the application that still have running or
/// waiting process instances pinned to them, with the fields their stored entities have. Provisioning calls it
/// only when its plan holds a destructive change, and only after it took the schema lock and the start lock, so
/// no instance starts on an older release while it counts.
/// </param>
/// <param name="SetActiveReleaseAsync">
/// Writes the active release and its site paths in the given provisioning transaction, and gives the
/// diagnostics of a refused name or site path. It runs after the plan is applied, just before the
/// commit. When it is <see langword="null"/>, provisioning does not set the active release.
/// </param>
public sealed record MigrationContext(
    Guid ReleaseId,
    Func<CancellationToken, Task<IReadOnlyDictionary<string, Guid>>> ReadStoredEntityIdsAsync,
    Func<DbTransaction, CancellationToken, Task<IReadOnlyList<OldReleaseWork>>> ReadOldReleaseWorkAsync,
    Func<DbTransaction, CancellationToken, Task<IReadOnlyList<Diagnostic>>>? SetActiveReleaseAsync)
{
    /// <summary>
    /// Reads the application's active release and the fingerprints of its computed definitions. Provisioning
    /// calls it under the schema lock, after the additive statements, and recomputes the stored computed values
    /// of each entity whose fingerprint in the release being activated is missing from them or differs. It
    /// recomputes nothing when the active release is the one being activated. When it is <see langword="null"/>,
    /// provisioning recomputes nothing.
    /// </summary>
    public Func<CancellationToken, Task<ActiveComputedFingerprints>>? ReadActiveComputedFingerprintsAsync { get; init; }

    /// <summary>
    /// Checks the module copies of the release being activated against the application's active release and gives
    /// the diagnostics of a module upgrade that is not additive. Provisioning calls it under the schema lock, right
    /// after the migration history check, whether or not a migration is pending. A diagnostic stops the activation and
    /// nothing is applied. When it is <see langword="null"/>, provisioning checks nothing.
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyList<Diagnostic>>>? CheckModuleUpgradeAsync { get; init; }

    /// <summary>
    /// The most rows of one entity that provisioning recomputes inside the activation transaction. An entity with
    /// more affected rows is marked pending and queued for the worker to backfill in batches.
    /// </summary>
    public int InlineRecomputeLimit { get; init; } = ReleaseActivator.DefaultInlineRecomputeLimit;

    /// <summary>
    /// The <c>statement_timeout</c> of the provisioning transaction, set with <c>SET LOCAL</c> semantics right after the
    /// transaction begins, so a wait for the schema lock counts too. A statement past it fails provisioning.
    /// </summary>
    public TimeSpan ActivationTimeout { get; init; } = ReleaseActivator.DefaultActivationTimeout;

    /// <summary>The tenant whose database is activated. A pending recompute queues its work item for it.</summary>
    public string? TenantId { get; init; }

    /// <summary>No release, no stored entities and no active release write.</summary>
    public static MigrationContext None { get; } = new(
        Guid.Empty,
        _ => Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)),
        (_, _) => Task.FromResult<IReadOnlyList<OldReleaseWork>>([]),
        null);
}

/// <summary>
/// The release that is active for an application and its fingerprints by entity id.
/// </summary>
/// <param name="ActiveReleaseId">The active release, or <see langword="null"/> before the first activation.</param>
/// <param name="Fingerprints">
/// The fingerprints the active release stores. A release stored before fingerprints existed has none, and
/// then every entity counts as changed.
/// </param>
public sealed record ActiveComputedFingerprints(Guid? ActiveReleaseId, IReadOnlyDictionary<Guid, string> Fingerprints);

/// <summary>
/// A release other than the one being activated that has running or waiting process instances pinned to it.
/// </summary>
/// <param name="ReleaseId">The release the instances are pinned to.</param>
/// <param name="OpenInstances">The number of running and waiting instances.</param>
/// <param name="FieldsByEntityId">
/// The field names of each entity of the release's stored model by entity id, ignoring letter case. It is
/// <see langword="null"/> when the stored release was not found, and then the release counts as using every column.
/// </param>
public sealed record OldReleaseWork(Guid ReleaseId, int OpenInstances, IReadOnlyDictionary<Guid, IReadOnlySet<string>>? FieldsByEntityId);
