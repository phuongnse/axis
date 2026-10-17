using Axis.Configuration.Model;
using Axis.Core.Telemetry;
using Axis.Data.Naming;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>
/// The backfill of stored computed values that activation leaves to the worker when a release would
/// recompute more rows of an entity than the inline limit allows. Activation marks the entity pending
/// with <see cref="MarkPendingAsync"/>, in its own transaction, and touches no row. The worker then runs
/// <see cref="RunBatchAsync"/> once per claim of the <see cref="WorkItemKind"/> work item, and ends the
/// backfill with <see cref="TryEndAsync"/>. A later activation restarts a pending entity with the same
/// <see cref="MarkPendingAsync"/>, and a record write of work pinned to an older release marks an entity that
/// is not pending with <see cref="MarkStaleWriteAsync"/>.
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
    /// always <paramref name="releaseId"/>. When the entity is already pending, the row is restarted: its
    /// target becomes <paramref name="releaseId"/>, its current releases are replaced and its fields are merged
    /// with the new ones. The work item is queued only when the entity has none.
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
        var current = await CurrentReleaseIdsAsync(connection, transaction, owner.Id, releaseId, fingerprint, cancellationToken);
        await using (var upsert = new NpgsqlCommand(
            """
            INSERT INTO axis.pending_computed_fields (entity_id, application_id, target_release_id, current_release_ids, fields, created_at)
            VALUES (@entity, @application, @release, @current, @fields, now())
            ON CONFLICT (entity_id) DO UPDATE SET
                target_release_id = EXCLUDED.target_release_id,
                current_release_ids = EXCLUDED.current_release_ids,
                fields = ARRAY(SELECT DISTINCT f FROM unnest(axis.pending_computed_fields.fields || EXCLUDED.fields) f ORDER BY f)
            """,
            connection,
            transaction))
        {
            upsert.Parameters.AddWithValue("entity", owner.Id);
            upsert.Parameters.AddWithValue("application", application.Manifest.Id);
            upsert.Parameters.AddWithValue("release", releaseId);
            upsert.Parameters.AddWithValue("current", current);
            upsert.Parameters.AddWithValue("fields", PendingFields(application, owner));
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnqueueAsync(connection, transaction, tenantId, owner.Id, cancellationToken);
    }

    /// <summary>
    /// Marks <paramref name="owner"/> pending again after a record write that ran with the release
    /// <paramref name="releaseId"/>, in the write's own <paramref name="transaction"/>. It does nothing when the
    /// application has no active release, when <paramref name="releaseId"/> is the active release, or when the
    /// two releases store the same computed fingerprint for the entity. A release with no stored fingerprint
    /// counts as different. The caller has already written the record, so it holds the record row lock. The
    /// method then takes a <c>FOR KEY SHARE</c> lock on the pending row, which conflicts only with the delete of
    /// the backfill's last batch, so a write cannot slip between that batch's last check and its delete. When
    /// there is no pending row it inserts one that targets the active release and lists the computed fields of
    /// <paramref name="application"/>, then queues the backfill work item with the write's trace and span ids.
    /// </summary>
    public static async Task MarkStaleWriteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel application,
        EntityModel owner,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        Guid active;
        string? activeFingerprint;
        await using (var read = new NpgsqlCommand(
            """
            SELECT a.release_id, fa.fingerprint, fw.fingerprint
            FROM axis.active_releases a
            LEFT JOIN axis.release_computed_fingerprints fa ON fa.release_id = a.release_id AND fa.entity_id = @entity
            LEFT JOIN axis.release_computed_fingerprints fw ON fw.release_id = @release AND fw.entity_id = @entity
            WHERE a.application_id = @application
            """,
            connection,
            transaction))
        {
            read.Parameters.AddWithValue("entity", owner.Id);
            read.Parameters.AddWithValue("release", releaseId);
            read.Parameters.AddWithValue("application", application.Manifest.Id);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            active = reader.GetGuid(0);
            activeFingerprint = reader.IsDBNull(1) ? null : reader.GetString(1);
            var writeFingerprint = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (active == releaseId || (activeFingerprint is not null && activeFingerprint == writeFingerprint))
            {
                return;
            }
        }

        await using (var share = new NpgsqlCommand("SELECT 1 FROM axis.pending_computed_fields WHERE entity_id = @entity FOR KEY SHARE", connection, transaction))
        {
            share.Parameters.AddWithValue("entity", owner.Id);
            if (await share.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return;
            }
        }

        var current = await CurrentReleaseIdsAsync(connection, transaction, owner.Id, active, activeFingerprint, cancellationToken);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.pending_computed_fields (entity_id, application_id, target_release_id, current_release_ids, fields, created_at)
            VALUES (@entity, @application, @release, @current, @fields, now())
            ON CONFLICT (entity_id) DO NOTHING
            """,
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("entity", owner.Id);
            insert.Parameters.AddWithValue("application", application.Manifest.Id);
            insert.Parameters.AddWithValue("release", active);
            insert.Parameters.AddWithValue("current", current);
            insert.Parameters.AddWithValue("fields", PendingFields(application, owner));
            if (await insert.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        await EnqueueAsync(connection, transaction, tenantId, owner.Id, cancellationToken);
    }

    /// <summary>
    /// Returns the release the backfill of a pending entity computes with, and the releases whose rows are up to
    /// date. It is the application's active release, even when the stored target is older: a write can insert
    /// a pending row while an activation is still committing, and computing with the old target would roll the
    /// table back. The current releases of the active release are then those whose stored fingerprint for the
    /// entity equals the active release's. Without an active release, or when it is the stored target, the stored
    /// values are returned.
    /// </summary>
    public static async Task<(Guid Target, Guid[] Current)> ResolveTargetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid entityId,
        Guid applicationId,
        Guid storedTarget,
        Guid[] storedCurrent,
        CancellationToken cancellationToken)
    {
        Guid active;
        await using (var read = new NpgsqlCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @application", connection, transaction))
        {
            read.Parameters.AddWithValue("application", applicationId);
            if (await read.ExecuteScalarAsync(cancellationToken) is not Guid found || found == storedTarget)
            {
                return (storedTarget, storedCurrent);
            }

            active = found;
        }

        string? fingerprint;
        await using (var read = new NpgsqlCommand("SELECT fingerprint FROM axis.release_computed_fingerprints WHERE release_id = @release AND entity_id = @entity", connection, transaction))
        {
            read.Parameters.AddWithValue("release", active);
            read.Parameters.AddWithValue("entity", entityId);
            fingerprint = await read.ExecuteScalarAsync(cancellationToken) as string;
        }

        return (active, await CurrentReleaseIdsAsync(connection, transaction, entityId, active, fingerprint, cancellationToken));
    }

    /// <summary>
    /// Ends the backfill of <paramref name="entity"/> if no row is out of date: it locks the pending row
    /// <c>FOR UPDATE</c>, checks the table again and deletes the row, and returns <see langword="true"/>. It
    /// returns <see langword="true"/> too when there is no pending row. It returns <see langword="false"/>
    /// when a row is out of date, and then the row stays. The lock waits for a write that holds the pending row
    /// <c>FOR KEY SHARE</c>, and that write's row is seen by the check. A write that comes later finds no
    /// pending row and inserts a new one. The out-of-date rows are those of the releases in
    /// <paramref name="current"/>, or of <see cref="ResolveTargetAsync"/> when it is <see langword="null"/>.
    /// The work item is never deleted here.
    /// </summary>
    public static async Task<bool> TryEndAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel entity,
        CancellationToken cancellationToken,
        Guid[]? current = null)
    {
        Guid applicationId;
        Guid storedTarget;
        Guid[] storedCurrent;
        await using (var read = new NpgsqlCommand(
            "SELECT application_id, target_release_id, current_release_ids FROM axis.pending_computed_fields WHERE entity_id = @entity FOR UPDATE",
            connection,
            transaction))
        {
            read.Parameters.AddWithValue("entity", entity.Id);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return true;
            }

            applicationId = reader.GetGuid(0);
            storedTarget = reader.GetGuid(1);
            storedCurrent = reader.GetFieldValue<Guid[]>(2);
        }

        current ??= (await ResolveTargetAsync(connection, transaction, entity.Id, applicationId, storedTarget, storedCurrent, cancellationToken)).Current;
        await using (var exists = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {RecordQueries.Table(entity)} WHERE {OutOfDate})", connection, transaction))
        {
            exists.Parameters.AddWithValue("current", current);
            if ((bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
            {
                return false;
            }
        }

        await DeleteAsync(connection, transaction, entity.Id, cancellationToken);
        return true;
    }

    /// <summary>Deletes the pending row of <paramref name="entityId"/>, if it has one.</summary>
    public static async Task DeleteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid entityId, CancellationToken cancellationToken)
    {
        await using var delete = new NpgsqlCommand("DELETE FROM axis.pending_computed_fields WHERE entity_id = @entity", connection, transaction);
        delete.Parameters.AddWithValue("entity", entityId);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The releases whose rows are up to date for <paramref name="entityId"/>: those whose stored fingerprint
    /// is <paramref name="fingerprint"/>, and always <paramref name="releaseId"/>. A <see langword="null"/>
    /// fingerprint matches nothing, so only <paramref name="releaseId"/> is current.
    /// </summary>
    public static async Task<Guid[]> CurrentReleaseIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid entityId,
        Guid releaseId,
        string? fingerprint,
        CancellationToken cancellationToken)
    {
        var current = new List<Guid>();
        if (fingerprint is not null)
        {
            await using var read = new NpgsqlCommand("SELECT release_id FROM axis.release_computed_fingerprints WHERE entity_id = @entity AND fingerprint = @fingerprint", connection, transaction);
            read.Parameters.AddWithValue("entity", entityId);
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

        return [.. current];
    }

    /// <summary>
    /// The number of rows of <paramref name="owner"/> that are out of date for <paramref name="current"/>, the
    /// releases whose rows are up to date.
    /// </summary>
    public static async Task<long> CountOutOfDateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel owner,
        Guid[] current,
        CancellationToken cancellationToken)
    {
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {RecordQueries.Table(owner)} WHERE {OutOfDate}", connection, transaction);
        count.Parameters.AddWithValue("current", current);
        return (long)(await count.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The ids of every row of <paramref name="owner"/> that is out of date for <paramref name="current"/>, in id order.</summary>
    public static Task<List<Guid>> ReadOutOfDateIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel owner,
        Guid[] current,
        CancellationToken cancellationToken) =>
        ComputedRecompute.ReadIdsAsync(
            connection,
            transaction,
            $"SELECT {IdColumn} FROM {RecordQueries.Table(owner)} WHERE {OutOfDate} ORDER BY {IdColumn}",
            parameters => parameters.AddWithValue("current", current),
            cancellationToken);

    // Inserts the backfill work item of the entity, unless it has one. It stores the trace and span ids of the
    // caller, so the worker's batches link back to the activation or write that queued them.
    private static async Task EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        await using var enqueue = new NpgsqlCommand(
            """
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, entity_id, trace_id, span_id)
            SELECT @id, @tenant, @kind, now(), @entity, @trace, @span
            WHERE NOT EXISTS (SELECT 1 FROM axis.process_work_items WHERE kind = @kind AND entity_id = @entity)
            """,
            connection,
            transaction);
        enqueue.Parameters.AddWithValue("id", Guid.CreateVersion7());
        enqueue.Parameters.AddWithValue("tenant", tenantId);
        enqueue.Parameters.AddWithValue("kind", WorkItemKind);
        enqueue.Parameters.AddWithValue("entity", entityId);
        enqueue.Parameters.Add(new NpgsqlParameter("trace", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentTraceId() ?? DBNull.Value });
        enqueue.Parameters.Add(new NpgsqlParameter("span", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentSpanId() ?? DBNull.Value });
        await enqueue.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string[] PendingFields(ApplicationModel application, EntityModel owner) =>
        owner.Fields.Where(field => field.IsComputed)
            .Concat(owner.Fields
                .Where(field => field.Type == FieldType.ChildCollection)
                .Select(field => application.FindEntity(field.Target!.Id) ?? throw new InvalidOperationException("The application has no entity for the child collection."))
                .SelectMany(child => child.Fields.Where(field => field.IsComputed)))
            .Select(field => field.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string IdColumn => EntityNaming.Quote(EntityNaming.IdColumn);

    // A row is out of date when its stamp is NULL or names a release that is not one of @current.
    private static string OutOfDate
    {
        get
        {
            var stamp = EntityNaming.Quote(EntityNaming.ComputedReleaseColumn);
            return $"{stamp} IS NULL OR {stamp} <> ALL(@current)";
        }
    }

    /// <summary>
    /// The query that reads the ids of the next batch of out-of-date rows of <paramref name="owner"/>. It binds
    /// <c>@current</c>, the releases whose rows are up to date, as a <c>uuid[]</c>.
    /// </summary>
    internal static string OutOfDateBatchSql(EntityModel owner) =>
        $"SELECT {IdColumn} FROM {RecordQueries.Table(owner)} WHERE {OutOfDate} ORDER BY {IdColumn} LIMIT {BatchSize}";

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
        var ids = await ComputedRecompute.ReadIdsAsync(
            connection,
            transaction,
            OutOfDateBatchSql(owner),
            parameters => parameters.AddWithValue("current", currentReleaseIds.ToArray()),
            cancellationToken);
        await ComputedRecompute.RecomputeIdsAsync(connection, transaction, target, owner, targetReleaseId, ids, cancellationToken);
        return ids.Count;
    }
}
