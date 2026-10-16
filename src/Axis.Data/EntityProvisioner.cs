using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Data.DataSources;
using Axis.Data.Naming;
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
/// provisioning records commit together, or neither does.
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
/// </summary>
public static class EntityProvisioner
{
    /// <summary>The <c>pg_advisory_xact_lock</c> key every entity schema change takes.</summary>
    public const long SchemaLockKey = 0x4158_4953_5343_4845; // "AXISSCHE" in ASCII.

    /// <summary>
    /// Plans <paramref name="model"/> against the tenant database behind <paramref name="context"/>
    /// and applies the plan when it has no diagnostics. The model must have no migrations. Call it
    /// outside an explicit transaction.
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
    /// is gone gives a diagnostic and nothing is applied. Call it outside an explicit transaction.
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

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

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
                await transaction.RollbackAsync(cancellationToken);
                return new ProvisioningResult(history.Diagnostics, []);
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
                await transaction.RollbackAsync(cancellationToken);
                return new ProvisioningResult(plan.Diagnostics, []);
            }

            if (plan.DestructiveChanges.Count > 0)
            {
                var blocked = CheckOldReleaseWork(plan.DestructiveChanges, await migrations.ReadOldReleaseWorkAsync(npgsqlTransaction, cancellationToken));
                if (blocked.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
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
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return ConvertFailed(
                        command.Convert,
                        $"The convert of '{command.Convert.Field}' fails for a stored record ({exception.SqlState}). Change the value so it converts every record.");
                }

                if (counted > 0 && command.Fill is { } fill)
                {
                    await transaction.RollbackAsync(cancellationToken);
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
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return ConvertFailed(convert, $"The convert of '{convert.Field}' leaves {counted} records with a value the field does not list.");
                }
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
            }));
            await context.SaveChangesAsync(cancellationToken);

            var activeReleaseSet = migrations.SetActiveReleaseAsync is not null;
            if (migrations.SetActiveReleaseAsync is { } setActiveRelease)
            {
                var refused = await setActiveRelease(transaction.GetDbTransaction(), cancellationToken);
                if (refused.Count > 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                    return new ProvisioningResult(refused, []);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProvisioningResult([], plan.Statements) { ActiveReleaseSet = activeReleaseSet };
        }
        catch
        {
            // The transaction rolls back on dispose; forget records that were never stored.
            context.ChangeTracker.Clear();
            throw;
        }
    }

    // One diagnostic per migration and release. A release blocks a change when its stored model has the changed
    // column, or when its stored model is gone, since nothing then shows that it does not use the column.
    private static List<Diagnostic> CheckOldReleaseWork(IReadOnlyList<DestructiveChange> changes, IReadOnlyList<OldReleaseWork> oldReleases)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var migration in changes.Select(change => change.Migration).DistinctBy(migration => migration.Id))
        {
            foreach (var release in oldReleases)
            {
                var blocks = changes.Any(change => change.Migration.Id == migration.Id && Blocks(change, release));
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

    private static bool Blocks(DestructiveChange change, OldReleaseWork release) =>
        release.FieldsByEntityId is not { } entities
        || (entities.TryGetValue(change.EntityId, out var fields) && (change.Field is null || fields.Contains(change.Field)));

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
