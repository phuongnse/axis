using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.DataSources;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Data.Schema;
using Axis.Data.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Data;

/// <summary>
/// Provisions the entity tables of a compiled application in the tenant database. Planning and
/// applying run in one transaction that holds one fixed advisory lock, so entity schema changes
/// are serialized and each plan is applied to the catalog it was made from. The DDL and the
/// provisioning records commit together, or neither does. A caller that already owns a transaction on the
/// context gets the same work under a savepoint instead, and commits or rolls back itself.
/// <para>
/// The lock does not block runtime writes to entity tables, so a statement can still fail, for
/// example adding a <c>NOT NULL</c> column after a row was inserted following the catalog read.
/// Any failing statement rolls back the whole transaction, DDL and records, and the exception
/// propagates.
/// </para>
/// <para>
/// The migrations of the model that no <c>axis.applied_migrations</c> row covers are pending. Their
/// <c>renameField</c>, <c>dropField</c>, <c>dropEntity</c>, <c>fill</c> and <c>convert</c> changes run first. A fill
/// that leaves a null, or a convert that fails for a row, rolls everything back with a diagnostic. Each pending
/// migration gets a row when the transaction commits. When the migration context sets the active
/// release, it is written in this transaction too, under the same lock as the history check. A
/// refused name or site path rolls everything back, and no other activation can commit a migration
/// between the check and the write.
/// </para>
/// <para>
/// Right after the history check, when the migration context can check the module upgrade, a module copy that
/// drops an entity or a field of the active release's copy, or changes a field's type, refuses the activation
/// with <see cref="DiagnosticCodes.NonAdditiveModuleUpgrade"/> and nothing is applied.
/// </para>
/// <para>
/// With a pending migration the transaction also takes the application's <see cref="StartLock"/>
/// exclusively, right after the history check, and holds it until commit. A process start that took
/// it shared commits first, and one that did not yet waits and then pins the new release. An
/// activation with no pending migration never takes it.
/// </para>
/// <para>
/// When the plan holds a destructive change, a <c>renameField</c>, <c>dropField</c>, <c>dropEntity</c> or
/// <c>convert</c> that plans a statement, the transaction asks the migration context for the other releases
/// that still have running or waiting process instances. Such a release that has the changed column in its stored
/// model refuses the activation with <see cref="DiagnosticCodes.OldReleaseWorkOpen"/> before any statement runs.
/// </para>
/// <para>
/// After the additive statements, when the migration context can read the active release's fingerprints, the
/// transaction recomputes the stored computed values of each entity whose computed definitions differ from the
/// active release's, with the release being activated. A computed field that fails for a record gets <c>NULL</c>
/// and a row in <c>axis.computed_field_errors</c>, and the activation goes on. Activating the active release again
/// recomputes nothing. A <c>fill</c> or <c>convert</c> clears the release stamp of the rows it changes, and of the
/// owner of each changed child row. The transaction then recomputes the rows of the touched entities that have no
/// stamp, unless it already recomputed the whole entity.
/// </para>
/// </summary>
public static class EntityProvisioner
{
    /// <summary>The <c>pg_advisory_xact_lock</c> key every entity schema change takes.</summary>
    public const long SchemaLockKey = 0x4158_4953_5343_4845; // "AXISSCHE" in ASCII.

    /// <summary>
    /// Plans <paramref name="model"/> against the tenant database behind <paramref name="context"/>
    /// and applies the plan when it has no diagnostics. The model must have no migrations. See the
    /// migration overload for the transaction it runs in.
    /// </summary>
    public static Task<ProvisioningResult> ProvisionAsync(
        ApplicationModel model,
        DataDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Migrations.Count > 0)
        {
            throw new ArgumentException("A model with migrations can only be provisioned with a migration context.", nameof(model));
        }

        return ProvisionAsync(model, context, MigrationContext.None, cancellationToken);
    }

    /// <summary>
    /// Plans <paramref name="model"/> against the tenant database behind <paramref name="context"/>,
    /// applying its pending migrations first, and applies the plan when it has no diagnostics. When
    /// <paramref name="migrations"/> can set the active release, it then writes it through
    /// <see cref="MigrationContext.SetActiveReleaseAsync"/> before it commits, and sets
    /// <see cref="ProvisioningResult.ActiveReleaseSet"/>. An applied migration whose file changed or
    /// is gone gives a diagnostic and nothing is applied. It starts its own transaction and commits it. When
    /// <paramref name="context"/> already has a transaction, the caller owns it: the work runs under a savepoint, a
    /// refusal rolls back to the savepoint, and a success releases the savepoint without committing. The caller's
    /// transaction stays usable after a refusal. After an exception, it is the caller's to roll back.
    /// </summary>
    public static async Task<ProvisioningResult> ProvisionAsync(
        ApplicationModel model,
        DataDbContext context,
        MigrationContext migrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(migrations);

        const string savepoint = "axis_provision";
        var outer = context.Database.CurrentTransaction;
        await using var owned = outer is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        var transaction = owned ?? outer!;
        if (outer is not null)
        {
            await outer.CreateSavepointAsync(savepoint, cancellationToken);
        }

        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        // A refusal undoes the work: the whole transaction when this method owns it, else back to the savepoint.
        Task UndoAsync() =>
            owned is not null ? owned.RollbackAsync(cancellationToken) : outer!.RollbackToSavepointAsync(savepoint, cancellationToken);

        try
        {
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, npgsqlTransaction))
            {
                command.Parameters.AddWithValue("key", SchemaLockKey);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await ExecuteAsync(connection, npgsqlTransaction, $"CREATE SCHEMA IF NOT EXISTS {EntityNaming.Quote(EntityNaming.Schema)}", cancellationToken);

            var history = CheckHistory(model, await ReadAppliedMigrationsAsync(model, context, cancellationToken));
            if (history.Pending.Count > 0)
            {
                // Taken before any diagnostic is acted on, since only the read above tells whether a migration is pending.
                // The schema lock keeps the rows from changing in between. Open starts finish first, and new ones wait.
                await StartLock.TakeExclusiveAsync(npgsqlTransaction, model.Manifest.Id, cancellationToken);
            }

            if (history.Diagnostics.Count > 0)
            {
                await UndoAsync();
                return new ProvisioningResult(history.Diagnostics, []);
            }

            if (migrations.CheckModuleUpgradeAsync is { } checkModuleUpgrade)
            {
                var upgrade = await checkModuleUpgrade(cancellationToken);
                if (upgrade.Count > 0)
                {
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return new ProvisioningResult(upgrade, []);
                }
            }

            var catalog = await CatalogReader.ReadAsync(connection, npgsqlTransaction, cancellationToken);
            var records = await ReadRecordsAsync(model, context, cancellationToken);
            // Read under the lock: an activation that held it before us may have renamed or dropped entities.
            var storedEntityIds = history.Pending.Count > 0
                ? await migrations.ReadStoredEntityIdsAsync(cancellationToken)
                : new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var plan = SchemaPlanner.Plan(model, catalog, records, history.Pending, storedEntityIds);
            if (plan.Diagnostics.Count > 0)
            {
                await UndoAsync();
                return new ProvisioningResult(plan.Diagnostics, []);
            }

            if (plan.DestructiveChanges.Count > 0)
            {
                var blocked = CheckOldReleaseWork(plan.DestructiveChanges, await migrations.ReadOldReleaseWorkAsync(npgsqlTransaction, cancellationToken));
                if (blocked.Count > 0)
                {
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return new ProvisioningResult(blocked, []);
                }
            }

            foreach (var command in plan.Commands)
            {
                long counted;
                try
                {
                    counted = await ExecuteAsync(connection, npgsqlTransaction, command, cancellationToken);
                }
                catch (PostgresException exception) when (command.Convert is not null)
                {
                    // The message is fixed: PostgreSQL's own text can quote a stored value.
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return ConvertFailed(
                        command.Convert,
                        $"The convert of '{command.Convert.Field}' fails for a stored record ({exception.SqlState}). Change the value so it converts every record.");
                }

                if (counted > 0 && command.Fill is { } fill)
                {
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return new ProvisioningResult(
                        [
                            new Diagnostic(
                                DiagnosticCodes.FillLeftNulls,
                                $"The fill of '{fill.Field}' gives null for {counted} records. Change the value so it gives a value for every record.",
                                fill.File,
                                $"/changes/{fill.ChangeIndex}/value",
                                fill.MigrationId),
                        ],
                        []);
                }

                if (counted > 0 && command.Convert is { CountsDisallowed: true } convert)
                {
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return ConvertFailed(convert, $"The convert of '{convert.Field}' leaves {counted} records with a value the field does not list.");
                }
            }

            if (migrations.ReadActiveComputedFingerprintsAsync is { } readActiveFingerprints)
            {
                await RecomputeChangedEntitiesAsync(model, migrations, plan.RecomputedEntityIds, await readActiveFingerprints(cancellationToken), connection, npgsqlTransaction, cancellationToken);
            }

            foreach (var change in plan.RecordChanges)
            {
                await ApplyRecordChangeAsync(context, change, cancellationToken);
            }

            context.ProvisionedEntities.AddRange(plan.NewEntities.Select(entity => new ProvisionedEntityRow
            {
                EntityId = entity.EntityId,
                ApplicationId = entity.ApplicationId,
                TableName = entity.TableName,
            }));
            context.ProvisionedEnumValues.AddRange(plan.NewEnumValues.Select(value => new ProvisionedEnumValueRow
            {
                EntityId = value.EntityId,
                FieldName = value.FieldName,
                Value = value.Value,
            }));
            var appliedAt = DateTimeOffset.UtcNow;
            context.AppliedMigrations.AddRange(history.Pending.Select(migration => new AppliedMigrationRow
            {
                MigrationId = migration.Id,
                ApplicationId = model.Manifest.Id,
                ReleaseId = migrations.ReleaseId,
                ContentHash = migration.ContentHash,
                AppliedAt = appliedAt,
                ChangedColumns = ChangedColumnsJson(plan.DestructiveChanges, migration),
            }));
            await context.SaveChangesAsync(cancellationToken);

            var activeReleaseSet = migrations.SetActiveReleaseAsync is not null;
            if (migrations.SetActiveReleaseAsync is { } setActiveRelease)
            {
                var refused = await setActiveRelease(transaction.GetDbTransaction(), cancellationToken);
                if (refused.Count > 0)
                {
                    await UndoAsync();
                    context.ChangeTracker.Clear();
                    return new ProvisioningResult(refused, []);
                }
            }

            if (owned is not null)
            {
                await owned.CommitAsync(cancellationToken);
            }
            else
            {
                await outer!.ReleaseSavepointAsync(savepoint, cancellationToken);
            }

            return new ProvisioningResult([], plan.Statements) { ActiveReleaseSet = activeReleaseSet };
        }
        catch
        {
            // The transaction rolls back on dispose; forget records that were never stored.
            context.ChangeTracker.Clear();
            throw;
        }
    }

    // Step 7: recomputes the stored computed values of each entity whose computed definitions differ from the active
    // release's. A missing fingerprint counts as different, so the first activation of an application recomputes
    // every entity, which costs nothing on tables that are new. Activating the active release again recomputes nothing,
    // even when it stored no fingerprints, so a restart does not recompute every table. Then it recomputes the rows
    // with a cleared stamp in each entity that a fill or convert touched, except an entity it just recomputed whole.
    // An entity with more affected rows than the inline limit is not recomputed here. It is marked pending, and the
    // worker backfills it. The affected rows of a changed entity are all its rows, and those of a fill or convert
    // are the rows left with no stamp. An entity that is already pending restarts instead, whether or not its
    // fingerprint changed: the rows still out of date for the release being activated are counted, and the limit
    // applies to them. See RestartPendingAsync.
    private static async Task RecomputeChangedEntitiesAsync(
        ApplicationModel model,
        MigrationContext migrations,
        IReadOnlyList<Guid> migratedEntityIds,
        ActiveComputedFingerprints active,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var releaseId = migrations.ReleaseId;
        var limit = migrations.InlineRecomputeLimit;
        var fingerprints = ComputedFingerprints.Compute(model);
        var pending = await ReadPendingEntityIdsAsync(connection, transaction, model.Manifest.Id, cancellationToken);
        var recomputed = new HashSet<Guid>();
        if (active.ActiveReleaseId != releaseId)
        {
            foreach (var (entityId, fingerprint) in fingerprints)
            {
                var entity = model.FindEntity(entityId) ?? throw new InvalidOperationException("The model has no entity for its fingerprint.");
                if (pending.Contains(entityId))
                {
                    await RestartPendingAsync(connection, transaction, model, migrations, entity, fingerprint, cancellationToken);
                    recomputed.Add(entityId);
                    continue;
                }

                if (active.Fingerprints.TryGetValue(entityId, out var current) && current == fingerprint)
                {
                    continue;
                }

                if (await CountRowsAsync(connection, transaction, entity, unstampedOnly: false, cancellationToken) <= limit)
                {
                    await ComputedRecompute.RecomputeAsync(connection, transaction, model, entity, releaseId, cancellationToken);
                }
                else
                {
                    await MarkPendingAsync(connection, transaction, model, migrations, entity, fingerprint, cancellationToken);
                }

                recomputed.Add(entityId);
            }
        }

        foreach (var entityId in migratedEntityIds)
        {
            if (recomputed.Contains(entityId) || model.FindEntity(entityId) is not { } entity)
            {
                continue;
            }

            if (pending.Contains(entityId))
            {
                await RestartPendingAsync(connection, transaction, model, migrations, entity, fingerprints[entityId], cancellationToken);
            }
            else if (await CountRowsAsync(connection, transaction, entity, unstampedOnly: true, cancellationToken) <= limit)
            {
                await ComputedRecompute.RecomputeAsync(connection, transaction, model, entity, releaseId, cancellationToken, unstampedOnly: true);
            }
            else
            {
                await MarkPendingAsync(connection, transaction, model, migrations, entity, fingerprints[entityId], cancellationToken);
            }
        }
    }

    // Read without a lock. A write of an older release that marks an entity pending takes the start lock first,
    // so none can commit a new pending row while this activation holds the exclusive start lock.
    private static async Task<HashSet<Guid>> ReadPendingEntityIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var pending = new HashSet<Guid>();
        await using var read = new NpgsqlCommand("SELECT entity_id FROM axis.pending_computed_fields WHERE application_id = @application", connection, transaction);
        read.Parameters.AddWithValue("application", applicationId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending.Add(reader.GetGuid(0));
        }

        return pending;
    }

    // Restarts the backfill of a pending entity with the release being activated. The up-to-date releases are those
    // whose fingerprint for the entity equals this release's. When no more rows are out of date than the limit, the
    // activation recomputes them itself, and the entity stops being pending if none is left. Otherwise the pending
    // row targets this release, merges its fields, and the worker continues.
    private static async Task RestartPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel model,
        MigrationContext migrations,
        EntityModel entity,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var releaseId = migrations.ReleaseId;
        var current = await ComputedBackfill.CurrentReleaseIdsAsync(connection, transaction, entity.Id, releaseId, fingerprint, cancellationToken);
        if (await ComputedBackfill.CountOutOfDateAsync(connection, transaction, entity, current, cancellationToken) <= migrations.InlineRecomputeLimit)
        {
            var ids = await ComputedBackfill.ReadOutOfDateIdsAsync(connection, transaction, entity, current, cancellationToken);
            await ComputedRecompute.RecomputeIdsAsync(connection, transaction, model, entity, releaseId, ids, cancellationToken);
            if (await ComputedBackfill.TryEndAsync(connection, transaction, entity, cancellationToken, current))
            {
                await using var delete = new NpgsqlCommand("DELETE FROM axis.process_work_items WHERE kind = @kind AND entity_id = @entity", connection, transaction);
                delete.Parameters.AddWithValue("kind", ComputedBackfill.WorkItemKind);
                delete.Parameters.AddWithValue("entity", entity.Id);
                await delete.ExecuteNonQueryAsync(cancellationToken);
                return;
            }
        }

        await MarkPendingAsync(connection, transaction, model, migrations, entity, fingerprint, cancellationToken);
    }

    private static async Task<long> CountRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityModel entity,
        bool unstampedOnly,
        CancellationToken cancellationToken)
    {
        var unstamped = unstampedOnly ? $" WHERE {EntityNaming.Quote(EntityNaming.ComputedReleaseColumn)} IS NULL" : "";
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {RecordQueries.Table(entity)}{unstamped}", connection, transaction);
        return (long)(await count.ExecuteScalarAsync(cancellationToken))!;
    }

    private static Task MarkPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ApplicationModel model,
        MigrationContext migrations,
        EntityModel entity,
        string fingerprint,
        CancellationToken cancellationToken) =>
        ComputedBackfill.MarkPendingAsync(
            connection,
            transaction,
            migrations.TenantId ?? throw new InvalidOperationException("The migration context names no tenant, so a pending recompute cannot queue its work."),
            model,
            entity,
            migrations.ReleaseId,
            fingerprint,
            cancellationToken);

    // One diagnostic per migration and release. A release blocks a change when its stored model has the changed
    // column, or when its stored model is gone, since nothing then shows that it does not use the column.
    private static List<Diagnostic> CheckOldReleaseWork(IReadOnlyList<DestructiveChange> changes, IReadOnlyList<OldReleaseWork> oldReleases)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var migration in changes.Select(change => change.Migration).DistinctBy(migration => migration.Id))
        {
            foreach (var release in oldReleases)
            {
                var blocks = changes.Any(change => change.Migration.Id == migration.Id && ReleaseColumns.Uses(release.FieldsByEntityId, change.EntityId, change.Field));
                if (blocks)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.OldReleaseWorkOpen,
                        $"The migration '{migration.Name}' changes a column that release '{release.ReleaseId}' uses, and {release.OpenInstances} running or waiting process instances are pinned to it. Activate the release again once they end.",
                        migration.File,
                        "",
                        migration.Id));
                }
            }
        }

        return DiagnosticOrder.Sort(diagnostics);
    }

    // The columns the migration changed, which a retry of an older process instance reads back.
    private static string ChangedColumnsJson(IReadOnlyList<DestructiveChange> changes, MigrationModel migration) =>
        JsonSerializer.Serialize(
            changes.Where(change => change.Migration.Id == migration.Id).Select(change => new ChangedColumn(change.EntityId, change.Field)),
            ChangedColumn.JsonOptions);

    private static ProvisioningResult ConvertFailed(ConvertTarget convert, string message) =>
        new(
            [new Diagnostic(DiagnosticCodes.ConvertFailed, message, convert.File, $"/changes/{convert.ChangeIndex}/value", convert.MigrationId)],
            []);

    // The migration rows of this application, and those of the model's migrations whatever their application.
    private static Task<List<AppliedMigrationRow>> ReadAppliedMigrationsAsync(ApplicationModel model, DataDbContext context, CancellationToken cancellationToken)
    {
        var applicationId = model.Manifest.Id;
        var migrationIds = model.Migrations.Select(migration => migration.Id).ToList();
        return context.AppliedMigrations
            .AsNoTracking()
            .Where(row => row.ApplicationId == applicationId || migrationIds.Contains(row.MigrationId))
            .ToListAsync(cancellationToken);
    }

    // An applied migration must stay as it was: same file content, and still there.
    private static (List<Diagnostic> Diagnostics, List<MigrationModel> Pending) CheckHistory(ApplicationModel model, List<AppliedMigrationRow> applied)
    {
        var diagnostics = new List<Diagnostic>();
        var pending = new List<MigrationModel>();
        var rowsById = applied.ToDictionary(row => row.MigrationId);
        foreach (var migration in model.Migrations)
        {
            if (!rowsById.TryGetValue(migration.Id, out var row))
            {
                pending.Add(migration);
            }
            else if (row.ContentHash != migration.ContentHash)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ChangedMigrationHistory,
                    $"The migration '{migration.Name}' was already applied, and its file has changed. Restore the file as it was.",
                    migration.File,
                    "",
                    migration.Id));
            }
        }

        var modelIds = model.Migrations.Select(migration => migration.Id).ToHashSet();
        foreach (var row in applied)
        {
            if (row.ApplicationId == model.Manifest.Id && !modelIds.Contains(row.MigrationId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ChangedMigrationHistory,
                    $"The migration with id '{row.MigrationId}' was already applied, and its file is gone. Restore the file.",
                    model.Manifest.File,
                    "",
                    row.MigrationId));
            }
        }

        return (DiagnosticOrder.Sort(diagnostics).ToList(), pending);
    }

    // Executed directly, so they join the open transaction and need no change tracking.
    private static async Task ApplyRecordChangeAsync(DataDbContext context, MigrationRecordChange change, CancellationToken cancellationToken)
    {
        var entityId = change.EntityId;
        var field = change.Field?.ToLowerInvariant();
        switch (change.Type)
        {
            case MigrationChangeType.RenameField:
                await context.Database.ExecuteSqlAsync(
                    $"UPDATE axis.provisioned_enum_values SET field_name = {change.To} WHERE entity_id = {entityId} AND lower(field_name) = {field}",
                    cancellationToken);
                break;
            case MigrationChangeType.DropField:
                await context.Database.ExecuteSqlAsync(
                    $"DELETE FROM axis.provisioned_enum_values WHERE entity_id = {entityId} AND lower(field_name) = {field}",
                    cancellationToken);
                break;
            case MigrationChangeType.Convert:
                var removed = change.Values.ToArray();
                await context.Database.ExecuteSqlAsync(
                    $"DELETE FROM axis.provisioned_enum_values WHERE entity_id = {entityId} AND lower(field_name) = {field} AND value = ANY({removed})",
                    cancellationToken);
                break;
            case MigrationChangeType.DropEntity:
                // Its enum values go with it: the foreign key cascades.
                await context.Database.ExecuteSqlAsync(
                    $"DELETE FROM axis.provisioned_entities WHERE entity_id = {entityId}",
                    cancellationToken);
                break;
        }
    }

    // The records of this application, and those of the model's entities whatever their application.
    private static async Task<ProvisioningRecords> ReadRecordsAsync(ApplicationModel model, DataDbContext context, CancellationToken cancellationToken)
    {
        var applicationId = model.Manifest.Id;
        var entityIds = model.Entities.Select(entity => entity.Id).ToList();

        var entities = await context.ProvisionedEntities
            .AsNoTracking()
            .Where(entity => entity.ApplicationId == applicationId || entityIds.Contains(entity.EntityId))
            .OrderBy(entity => entity.EntityId)
            .Select(entity => new ProvisionedEntity(entity.EntityId, entity.ApplicationId, entity.TableName))
            .ToListAsync(cancellationToken);
        var enumValues = await context.ProvisionedEnumValues
            .AsNoTracking()
            .Where(value => entityIds.Contains(value.EntityId))
            .Select(value => new ProvisionedEnumValue(value.EntityId, value.FieldName, value.Value))
            .ToListAsync(cancellationToken);

        return new ProvisioningRecords(entities, enumValues);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // A fill returns the number of rows it left null, and an enum convert the number of rows with an unlisted value.
    // Any other command returns 0.
    private static async Task<long> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, SchemaCommand schemaCommand, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(schemaCommand.Sql, connection, transaction);
        DataSourceQueries.AddValues(command, schemaCommand.Parameters);
        if (schemaCommand.Fill is null && schemaCommand.Convert is not { CountsDisallowed: true })
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return 0;
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }
}
