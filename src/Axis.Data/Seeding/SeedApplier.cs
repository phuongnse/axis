using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Data.Seeding;

/// <summary>
/// Inserts the seed records of an active application whose id is missing. A seed without
/// <see cref="SeedModel.Sync"/> leaves existing records alone, also when they were edited. A synced
/// seed updates an existing record whose declared values differ from the stored ones, through
/// <see cref="RecordCommands.UpdateAsync"/>, so its version grows by one; an identical record is not
/// written, undeclared fields keep their values, and no record is deleted. Only the owner's column
/// values are compared, so a synced record whose only change is in its child rows is not written.
/// Every write sets the record's computed fields, from the stored record and the declared values
/// on an update. A value that cannot be computed rolls back every write.
/// Seed values follow the
/// record API's rules: each record goes through <see cref="RecordInputParser"/> as a create body
/// and is inserted by <see cref="RecordCommands.CreateAsync"/>. Every record is parsed before any
/// write, and every parse problem is reported. The writes run in one transaction in path order of
/// the seed files and file order of the records, so a reference may name a record inserted earlier
/// in the same run. The first storage problem rolls back every write.
/// </summary>
public static class SeedApplier
{
    /// <summary>The message of a write refused by a constraint the active model does not declare.</summary>
    private const string SchemaConflictMessage = "The record could not be written because the stored schema does not match the active model.";

    /// <summary>
    /// Applies <paramref name="model"/>'s seeds over <paramref name="connection"/>, whose entity
    /// tables are provisioned for the model. Must run outside an explicit transaction.
    /// </summary>
    public static async Task<SeedResult> ApplyAsync(
        ApplicationModel model,
        NpgsqlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(connection);

        var diagnostics = new List<Diagnostic>();
        var parsed = new List<(SeedModel Seed, EntityModel Entity, int Index, Guid Id, RecordInput Input)>();
        foreach (var seed in model.Seeds)
        {
            var entity = model.Entities.Single(candidate => candidate.Id == seed.Entity.Id);
            for (var index = 0; index < seed.Records.Count; index++)
            {
                var record = seed.Records[index];
                var body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, JsonElement> { ["values"] = record.Values });
                var result = RecordInputParser.Parse(body, entity, model, RecordOperation.Create);
                if (result.Errors is { } errors)
                {
                    diagnostics.AddRange(errors.Select(error => Invalid(seed, $"/records/{index}{error.Key}", error.Value[0])));
                }
                else
                {
                    parsed.Add((seed, entity, index, record.Id, result.Input!));
                }
            }
        }

        if (diagnostics.Count > 0)
        {
            return new SeedResult(DiagnosticOrder.Sort(diagnostics), 0, 0);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inserted = 0;
        var updated = 0;
        foreach (var (seed, entity, index, id, input) in parsed)
        {
            RecordWriteResult result;
            var updating = false;
            if (seed.Sync)
            {
                // Only the declared values are compared, so a computed value alone never causes a write.
                var found = await RecordCommands.FindVersionAsync(connection, entity, id, input.Values, cancellationToken);
                if (found is { Differs: false })
                {
                    continue;
                }

                updating = found is not null;
                if (found is { } existing)
                {
                    // The seed declares only some values, so the others come from the stored record.
                    var stored = entity.HasComputedFields
                        ? await RecordQueries.GetAsync(connection, model, entity, id, cancellationToken)
                        : null;
                    var computed = RecordComputer.ComputeUpdate(model, entity, stored, input.Values, input.Rows);
                    if (computed.Errors is { } errors)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return NotComputed(seed, index, errors);
                    }

                    result = await RecordCommands.UpdateAsync(connection, model, entity, id, existing.Version, computed.Values, computed.Rows, cancellationToken);
                }
                else
                {
                    var computed = RecordComputer.ComputeCreate(model, entity, input.Values, input.Rows);
                    if (computed.Errors is { } errors)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return NotComputed(seed, index, errors);
                    }

                    result = await RecordCommands.CreateAsync(connection, model, entity, computed.Values, computed.Rows, id, cancellationToken);
                }
            }
            else
            {
                if (await RecordCommands.ExistsAsync(connection, RecordQueries.Table(entity), id, cancellationToken))
                {
                    continue;
                }

                var computed = RecordComputer.ComputeCreate(model, entity, input.Values, input.Rows);
                if (computed.Errors is { } errors)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return NotComputed(seed, index, errors);
                }

                result = await RecordCommands.CreateAsync(connection, model, entity, computed.Values, computed.Rows, id, cancellationToken);
            }

            if (result.Outcome != RecordWriteOutcome.Written)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failed(seed, index, result);
            }

            if (updating)
            {
                updated++;
            }
            else
            {
                inserted++;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new SeedResult([], inserted, updated);
    }

    /// <summary>Reports a refused write at the record's path, or at the pointers of its errors.</summary>
    private static SeedResult Failed(SeedModel seed, int index, RecordWriteResult result)
    {
        var path = $"/records/{index}";
        return new SeedResult(
            result.Errors is { } errors
                ? DiagnosticOrder.Sort(errors.Select(error => Invalid(seed, path + error.Key, error.Value[0])))
                : [Invalid(seed, path, SchemaConflictMessage)],
            0,
            0);
    }

    /// <summary>Reports a record whose computed fields could not be computed, at the pointers of its errors.</summary>
    private static SeedResult NotComputed(SeedModel seed, int index, SortedDictionary<string, string[]> errors) =>
        new(DiagnosticOrder.Sort(errors.Select(error => Invalid(seed, $"/records/{index}{error.Key}", error.Value[0]))), 0, 0);

    private static Diagnostic Invalid(SeedModel seed, string path, string message) =>
        new(DiagnosticCodes.InvalidSeedValue, message, seed.File, path, seed.Id);
}
