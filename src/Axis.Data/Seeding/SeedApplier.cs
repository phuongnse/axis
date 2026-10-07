using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Npgsql;

namespace Axis.Data.Seeding;

/// <summary>
/// Inserts the seed records of an active application whose id is missing, and leaves existing
/// records alone, also when they were edited. Seed values follow the record API's rules: each
/// record goes through <see cref="RecordInputParser"/> as a create body and is inserted by
/// <see cref="RecordCommands.CreateAsync"/>. Every record is parsed before any insert, and every
/// parse problem is reported. The inserts run in one transaction in path order of the seed files
/// and file order of the records, so a reference may name a record inserted earlier in the same
/// run. The first storage problem rolls back every insert.
/// </summary>
public static class SeedApplier
{
    /// <summary>The message of a create refused by a constraint the active model does not declare.</summary>
    private const string SchemaConflictMessage = "The record could not be inserted because the stored schema does not match the active model.";

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
                var result = RecordInputParser.Parse(body, entity, RecordOperation.Create);
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
            return new SeedResult(DiagnosticOrder.Sort(diagnostics), 0);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var inserted = 0;
        foreach (var (seed, entity, index, id, input) in parsed)
        {
            if (await RecordCommands.ExistsAsync(connection, RecordQueries.Table(entity), id, cancellationToken))
            {
                continue;
            }

            var result = await RecordCommands.CreateAsync(connection, entity, input.Values, id, cancellationToken);
            if (result.Outcome != RecordWriteOutcome.Written)
            {
                await transaction.RollbackAsync(cancellationToken);
                var path = $"/records/{index}";
                return new SeedResult(
                    result.Errors is { } errors
                        ? DiagnosticOrder.Sort(errors.Select(error => Invalid(seed, path + error.Key, error.Value[0])))
                        : [Invalid(seed, path, SchemaConflictMessage)],
                    0);
            }

            inserted++;
        }

        await transaction.CommitAsync(cancellationToken);
        return new SeedResult([], inserted);
    }

    private static Diagnostic Invalid(SeedModel seed, string path, string message) =>
        new(DiagnosticCodes.InvalidSeedValue, message, seed.File, path, seed.Id);
}
