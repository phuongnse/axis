using Axis.Core.Telemetry;
using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// Reads which computed fields are pending, from <c>axis.pending_computed_fields</c>. An entity is
/// pending, as a whole, from the activation that left its stored computed values for the worker to
/// recompute until the backfill ends. The record API refuses a sort, a search or a policy filter that
/// reads a pending field, because it would decide from a stale stored value.
/// </summary>
public static class PendingComputedFields
{
    /// <summary>
    /// Returns the pending computed field names of each of <paramref name="entityIds"/> that is pending,
    /// inside <paramref name="transaction"/> when it is not <see langword="null"/>. An entity that is not
    /// pending has no entry. The names are those of the entity and of its child entities.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, IReadOnlySet<string>>> ReadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(entityIds);

        var pending = new Dictionary<Guid, IReadOnlySet<string>>();
        if (entityIds.Count == 0)
        {
            return pending;
        }

        await using var command = new NpgsqlCommand(
            "SELECT entity_id, fields FROM axis.pending_computed_fields WHERE entity_id = ANY(@ids)",
            connection,
            transaction);
        command.Parameters.AddWithValue("ids", entityIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pending[reader.GetGuid(0)] = reader.GetFieldValue<string[]>(1).ToHashSet(StringComparer.Ordinal);
        }

        return pending;
    }

    /// <summary>
    /// Counts the pending computed field names of each application that has an active release, summed over
    /// its pending entities. An application with nothing pending counts 0. Pending rows of an application
    /// with no active release are left out.
    /// </summary>
    public static async Task<IReadOnlyList<PendingFieldsGauge>> CountByApplicationAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            """
            SELECT a.name, coalesce(sum(cardinality(p.fields)), 0)::bigint
            FROM axis.active_releases a
            LEFT JOIN axis.pending_computed_fields p ON p.application_id = a.application_id
            GROUP BY a.name
            """,
            connection);
        var rows = new List<PendingFieldsGauge>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new PendingFieldsGauge(reader.GetString(0), reader.GetInt64(1)));
        }

        return rows;
    }
}
