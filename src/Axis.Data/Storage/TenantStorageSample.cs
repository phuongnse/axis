using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// The bytes a tenant's storage holds at one moment, split so that no byte counts twice. Every stored file is
/// kept in <c>axis.file_contents</c> in the tenant database, so the database size already holds the files.
/// </summary>
public readonly record struct TenantStorageSample(long DatabaseBytes, long FileBytes)
{
    /// <summary>
    /// Measures the database of <paramref name="connection"/>. The file bytes are the on-disk size of
    /// <c>axis.file_contents</c> with its indexes and TOAST data. The database bytes are the rest of the database.
    /// A database without that table has no file bytes.
    /// </summary>
    public static async Task<TenantStorageSample> ReadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            "SELECT pg_database_size(current_database()), COALESCE(pg_total_relation_size(to_regclass('axis.file_contents')), 0)",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt64(0);
        var files = Math.Min(reader.GetInt64(1), total);
        return new TenantStorageSample(total - files, files);
    }
}
