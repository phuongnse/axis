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

    // A file attaches when it is pending and the caller uploaded it for this entity and field path,
    // or when it is already attached to this record at this path. RETURNING gives the ids that
    // matched, so each refused id can be told apart. A missing uploader never matches.
    private const string AttachSql =
        """
        UPDATE "axis"."files" AS f SET "state" = 'attached', "owner_record_id" = @record
        FROM unnest(@ids, @entities, @paths) AS t(id, entity_id, field_path)
        WHERE f.id = t.id AND f.application_id = @application AND f.entity_id = t.entity_id AND f.field_path = t.field_path
          AND ((f.state = 'pending' AND f.uploaded_by = @uploader) OR (f.state = 'attached' AND f.owner_record_id = @record))
        RETURNING f.id
        """;

    // The kept ids were attached at their paths by the attach step before this runs.
    private const string MarkReplacedSql =
        """
        UPDATE "axis"."files" SET "state" = 'deleted'
        WHERE "application_id" = @application AND "owner_record_id" = @record AND "state" = 'attached'
          AND "field_path" = ANY(@paths) AND NOT ("id" = ANY(@kept))
        """;

    private const string FindAttachedSql =
        """
        SELECT "entity_id", "field_path", "owner_record_id", "name", "media_type" FROM "axis"."files"
        WHERE "id" = @id AND "application_id" = @application AND "state" = 'attached'
        """;

    // Joined to the file, so a file that was deleted after it was found gives no bytes.
    private const string ReadContentSql =
        """
        SELECT c."content" FROM "axis"."file_contents" c JOIN "axis"."files" f ON f."id" = c."file_id"
        WHERE c."file_id" = @id AND f."state" = 'attached'
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

    /// <summary>
    /// Attaches each of <paramref name="files"/> to the record <paramref name="recordId"/> in one
    /// statement of the caller's transaction, and returns the ids that were attached or already
    /// attached there. A file qualifies when it is <c>pending</c> and <paramref name="uploader"/>
    /// uploaded it for the same entity and field path, or when it is already attached to this record
    /// at the same entity and field path. With no <paramref name="uploader"/>, only the second case
    /// qualifies. An id that is not returned is unknown, foreign, attached elsewhere or for another field.
    /// </summary>
    public static async Task<HashSet<Guid>> AttachAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid recordId,
        string? uploader,
        IReadOnlyList<(Guid Id, Guid EntityId, string Path)> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(files);

        await using var command = new NpgsqlCommand(AttachSql, connection);
        command.Parameters.AddWithValue("record", recordId);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.Add(new NpgsqlParameter("uploader", NpgsqlDbType.Text) { Value = (object?)uploader ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = files.Select(file => file.Id).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("entities", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = files.Select(file => file.EntityId).ToArray() });
        command.Parameters.Add(new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = files.Select(file => file.Path).ToArray() });

        var attached = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attached.Add(reader.GetGuid(0));
        }

        return attached;
    }

    /// <summary>
    /// Marks <c>deleted</c>, in one statement of the caller's transaction, every file attached to the
    /// record <paramref name="recordId"/> at one of <paramref name="paths"/> whose id is not in
    /// <paramref name="kept"/>. Call it after the write attached the files it sends, so each kept id
    /// is attached at its path. Does nothing without a path.
    /// </summary>
    public static async Task MarkReplacedAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid recordId,
        IReadOnlyList<string> paths,
        IReadOnlyList<Guid> kept,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(kept);

        if (paths.Count == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(MarkReplacedSql, connection);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("record", recordId);
        command.Parameters.Add(new NpgsqlParameter("paths", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = paths.ToArray() });
        command.Parameters.Add(new NpgsqlParameter("kept", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = kept.ToArray() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// The file with <paramref name="id"/> in the application <paramref name="applicationId"/>, or
    /// <see langword="null"/> when it is unknown, belongs to another application or is not <c>attached</c>.
    /// Runs in <paramref name="transaction"/> when it is not <see langword="null"/>.
    /// </summary>
    public static async Task<AttachedFile?> FindAttachedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid applicationId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(FindAttachedSql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AttachedFile(id, reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    /// <summary>
    /// The bytes of the <c>attached</c> file with <paramref name="id"/>, or <see langword="null"/> when it is
    /// unknown or no longer attached. Runs in <paramref name="transaction"/> when it is not <see langword="null"/>.
    /// </summary>
    public static async Task<byte[]?> ReadContentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(ReadContentSql, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) as byte[];
    }
}
