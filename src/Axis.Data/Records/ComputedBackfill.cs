using Axis.Configuration.Model;
using Axis.Data.Naming;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// The backfill of stored computed values that activation leaves to the worker when a release would
/// recompute more rows of an entity than the inline limit allows. Activation marks the entity pending
/// with <see cref="MarkPendingAsync"/>, in its own transaction, and touches no row. The worker then runs
/// <see cref="RunBatchAsync"/> once per claim of the <see cref="WorkItemKind"/> work item.
/// <para>
/// A row is out of date when its stamp is <c>NULL</c> or names a release that is not one of the current
/// releases of the pending entity. A batch takes the first <see cref="BatchSize"/> out-of-date rows by id,
/// so it keeps no cursor, and a row it skipped is taken by a later batch. A batch stores new values and the
/// stamp exactly as the recompute at activation does: it leaves <c>version</c> unchanged, writes no audit
/// record, starts no trigger, and never overwrites a row that a user changed after the batch read it.
/// </para>
/// </summary>
internal static class ComputedBackfill
{
    /// <summary>The kind of the work item that runs the batches of one pending entity.</summary>
    public const string WorkItemKind = "computed.backfill";

    /// <summary>The most rows one batch recomputes.</summary>
    public const int BatchSize = 500;

    /// <summary>
    /// Marks <paramref name="owner"/> pending for the release <paramref name="releaseId"/> and queues the
    /// work item that backfills it, on <paramref name="connection"/> inside <paramref name="transaction"/>.
    /// The pending row lists every computed field of the entity and of its child entities. The current
    /// releases are those whose stored fingerprint for the entity is <paramref name="fingerprint"/>, and
    /// always <paramref name="releaseId"/>. It throws when the entity is already pending.
    /// </summary>
    public static async Task MarkPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        EntityModel owner,
        Guid releaseId,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var current = new List<Guid>();
        await using (var read = new NpgsqlCommand("SELECT release_id FROM axis.release_computed_fingerprints WHERE entity_id = @entity AND fingerprint = @fingerprint", connection, transaction))
        {
            read.Parameters.AddWithValue("entity", owner.Id);
            read.Parameters.AddWithValue("fingerprint", fingerprint);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                current.Add(reader.GetGuid(0));
            }
        }

        if (!current.Contains(releaseId))
        {
            current.Add(releaseId);
        }

        var fields = owner.Fields.Where(field => field.IsComputed)
            .Concat(owner.Fields
                .Where(field => field.Type == FieldType.ChildCollection)
                .Select(field => application.FindEntity(field.Target!.Id) ?? throw new InvalidOperationException("The application has no entity for the child collection."))
                .SelectMany(child => child.Fields.Where(field => field.IsComputed)))
            .Select(field => field.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.pending_computed_fields (entity_id, application_id, target_release_id, current_release_ids, fields, created_at)
            VALUES (@entity, @application, @release, @current, @fields, now())
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("entity", owner.Id);
            insert.Parameters.AddWithValue("application", application.Manifest.Id);
            insert.Parameters.AddWithValue("release", releaseId);
            insert.Parameters.AddWithValue("current", current.ToArray());
            insert.Parameters.AddWithValue("fields", fields);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var enqueue = new NpgsqlCommand(
            "INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, entity_id) VALUES (@id, @tenant, @kind, now(), @entity)",
            connection,
            transaction);
        enqueue.Parameters.AddWithValue("id", Guid.CreateVersion7());
        enqueue.Parameters.AddWithValue("tenant", tenantId);
        enqueue.Parameters.AddWithValue("kind", WorkItemKind);
        enqueue.Parameters.AddWithValue("entity", owner.Id);
        await enqueue.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Recomputes one batch of the out-of-date rows of <paramref name="owner"/> with
    /// <paramref name="targetReleaseId"/>, from <paramref name="target"/>, on <paramref name="connection"/>
    /// inside <paramref name="transaction"/>. Returns the number of out-of-date rows it found, which is
    /// zero when the backfill is done.
    /// </summary>
    public static async Task<int> RunBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel target,
        EntityModel owner,
        IReadOnlyCollection<Guid> currentReleaseIds,
        Guid targetReleaseId,
        CancellationToken cancellationToken)
    {
        var idColumn = EntityNaming.Quote(EntityNaming.IdColumn);
        var stamp = EntityNaming.Quote(EntityNaming.ComputedReleaseColumn);
        var ids = await ComputedRecompute.ReadIdsAsync(
            connection,
            transaction,
            $"SELECT {idColumn} FROM {RecordQueries.Table(owner)} WHERE {stamp} IS NULL OR {stamp} <> ALL(@current) ORDER BY {idColumn} LIMIT {BatchSize}",
            parameters => parameters.AddWithValue("current", currentReleaseIds.ToArray()),
            cancellationToken);
        await ComputedRecompute.RecomputeIdsAsync(connection, transaction, target, owner, targetReleaseId, ids, cancellationToken);
        return ids.Count;
    }
}
