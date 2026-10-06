using Axis.Configuration.Model;
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
/// </summary>
public static class EntityProvisioner
{
    /// <summary>The <c>pg_advisory_xact_lock</c> key every entity schema change takes.</summary>
    public const long SchemaLockKey = 0x4158_4953_5343_4845; // "AXISSCHE" in ASCII.

    /// <summary>
    /// Plans <paramref name="model"/> against the tenant database behind <paramref name="context"/>
    /// and applies the plan when it has no diagnostics. Call it outside an explicit transaction.
    /// </summary>
    public static async Task<ProvisioningResult> ProvisionAsync(
        ApplicationModel model,
        DataDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);

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

            var catalog = await CatalogReader.ReadAsync(connection, npgsqlTransaction, cancellationToken);
            var records = await ReadRecordsAsync(model, context, cancellationToken);
            var plan = SchemaPlanner.Plan(model, catalog, records);
            if (plan.Diagnostics.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ProvisioningResult(plan.Diagnostics, []);
            }

            foreach (var statement in plan.Statements)
            {
                await ExecuteAsync(connection, npgsqlTransaction, statement, cancellationToken);
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
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProvisioningResult([], plan.Statements);
        }
        catch
        {
            // The transaction rolls back on dispose; forget records that were never stored.
            context.ChangeTracker.Clear();
            throw;
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
}
