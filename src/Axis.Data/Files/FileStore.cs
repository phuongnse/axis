using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Files;

/// <summary>Stores uploaded files in <c>axis.files</c> and <c>axis.file_contents</c> of the tenant database.</summary>
public static class FileStore
{
    private const string InsertFileSql =
        """
        INSERT INTO "axis"."files"
            ("id", "application_id", "entity_id", "field_path", "owner_record_id", "name", "media_type", "size", "sha256", "storage_kind", "state", "uploaded_by", "uploaded_at")
        VALUES (@id, @application, @entity, @path, NULL, @name, @mediaType, @size, @sha256, 'database', 'pending', @uploadedBy, @uploadedAt)
        """;

    private const string InsertContentSql = """INSERT INTO "axis"."file_contents" ("file_id", "content") VALUES (@id, @content)""";

    /// <summary>
    /// Stores <paramref name="file"/> as <c>pending</c> with no owner record, and its bytes, in one
    /// transaction of <paramref name="connection"/>, which must have none open.
    /// </summary>
    public static async Task InsertPendingAsync(
        NpgsqlConnection connection,
        PendingFile file,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(file);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = new NpgsqlCommand(InsertFileSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", file.Id);
            command.Parameters.AddWithValue("application", file.ApplicationId);
            command.Parameters.AddWithValue("entity", file.EntityId);
            command.Parameters.AddWithValue("path", file.FieldPath);
            command.Parameters.AddWithValue("name", file.Name);
            command.Parameters.AddWithValue("mediaType", file.MediaType);
            command.Parameters.AddWithValue("size", file.Size);
            command.Parameters.Add(new NpgsqlParameter("sha256", NpgsqlDbType.Bytea) { Value = file.Sha256 });
            command.Parameters.AddWithValue("uploadedBy", file.UploadedBy);
            command.Parameters.AddWithValue("uploadedAt", file.UploadedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(InsertContentSql, connection, transaction))
        {
            command.Parameters.AddWithValue("id", file.Id);
            command.Parameters.Add(new NpgsqlParameter("content", NpgsqlDbType.Bytea) { Value = content });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
