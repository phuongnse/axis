using System.Text.Json;
using Npgsql;

namespace Axis.Data;

/// <summary>Reads the columns that the applied migrations of an application changed.</summary>
public static class AppliedMigrationColumns
{
    /// <summary>
    /// Reads every applied migration of the application in the transaction. The columns are <see langword="null"/>
    /// for a row applied before they were recorded.
    /// </summary>
    public static async Task<IReadOnlyList<(Guid MigrationId, IReadOnlyList<ChangedColumn>? Changed)>> ReadAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            "SELECT migration_id, changed_columns FROM axis.applied_migrations WHERE application_id = @application",
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("application", applicationId);
        var applied = new List<(Guid, IReadOnlyList<ChangedColumn>?)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var changed = reader.IsDBNull(1)
                ? null
                : JsonSerializer.Deserialize<List<ChangedColumn>>(reader.GetString(1), ChangedColumn.JsonOptions);
            applied.Add((reader.GetGuid(0), changed));
        }

        return applied;
    }
}
