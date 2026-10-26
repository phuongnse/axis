using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Records;

/// <summary>The state of a record import.</summary>
public static class RecordImportStates
{
    /// <summary>The file is uploaded and its dry run has not ended.</summary>
    public const string Pending = "pending";

    /// <summary>The dry run found no error.</summary>
    public const string Checked = "checked";

    /// <summary>The dry run found errors. The file is cleared.</summary>
    public const string Failed = "failed";

    /// <summary>The records of the file are written.</summary>
    public const string Committed = "committed";
}

/// <summary>The format of an import file, which picks the reader that reads it.</summary>
public static class RecordImportFormats
{
    /// <summary>UTF-8 text in CSV form.</summary>
    public const string Csv = "csv";

    /// <summary>An <c>xlsx</c> workbook, read from its first sheet.</summary>
    public const string Xlsx = "xlsx";
}

/// <summary>A new import to store: the upload of a file by one person for one entity.</summary>
public sealed record RecordImport(
    Guid Id,
    string TenantId,
    Guid ApplicationId,
    Guid EntityId,
    string UploaderId,
    string UploaderName,
    IReadOnlyList<string> UploaderRoles,
    string? Key,
    string Format,
    byte[] File,
    Guid WorkItemId,
    DateTimeOffset CreatedAt);

/// <summary>An import as its uploader reads it. <see cref="Errors"/> is empty unless the import failed.</summary>
public sealed record RecordImportStatus(Guid Id, string State, string? Key, IReadOnlyDictionary<string, string[]> Errors, DateTimeOffset CreatedAt);

/// <summary>An import as the worker runs its dry run, with the file, its format and the uploader the checks run for.</summary>
public sealed record RecordImportJob(
    Guid Id,
    Guid ApplicationId,
    Guid EntityId,
    string UploaderId,
    string UploaderName,
    IReadOnlyList<string> UploaderRoles,
    string State,
    string Format,
    byte[]? File);

/// <summary>
/// The storage of record imports in <c>axis.record_imports</c>. An import is stored <c>pending</c> in the
/// same transaction that queues its <see cref="CheckWorkItemKind"/> work item, and the row names the item.
/// The worker locks the import through the item, checks the file, and ends the import <c>checked</c> or
/// <c>failed</c> with <see cref="FinishCheckAsync"/>. A failed import's file is cleared. Only the uploader
/// finds an import with <see cref="FindAsync"/>.
/// </summary>
public static class RecordImports
{
    /// <summary>The kind of the work item that runs the dry run of one import.</summary>
    public const string CheckWorkItemKind = "records.importCheck";

    /// <summary>Stores <paramref name="import"/> as a pending import.</summary>
    public static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, RecordImport import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(import);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.record_imports (id, tenant_id, application_id, entity_id, uploader_id, uploader_name, uploader_roles, key, state, format, file, work_item_id, created_at)
            VALUES (@id, @tenant, @application, @entity, @uploader, @name, @roles, @key, 'pending', @format, @file, @item, @created)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", import.Id);
        command.Parameters.AddWithValue("tenant", import.TenantId);
        command.Parameters.AddWithValue("application", import.ApplicationId);
        command.Parameters.AddWithValue("entity", import.EntityId);
        command.Parameters.AddWithValue("uploader", import.UploaderId);
        command.Parameters.AddWithValue("name", import.UploaderName);
        command.Parameters.AddWithValue("roles", import.UploaderRoles.ToArray());
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Text) { Value = (object?)import.Key ?? DBNull.Value });
        command.Parameters.AddWithValue("format", import.Format);
        command.Parameters.Add(new NpgsqlParameter("file", NpgsqlDbType.Bytea) { Value = import.File });
        command.Parameters.AddWithValue("item", import.WorkItemId);
        command.Parameters.AddWithValue("created", import.CreatedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads an import for its uploader, or returns <see langword="null"/> when <paramref name="id"/> is no import
    /// of the entity in the application, or when <paramref name="uploaderId"/> did not upload it. The errors come
    /// in the order of the rows they name.
    /// </summary>
    public static async Task<RecordImportStatus?> FindAsync(
        NpgsqlConnection connection,
        Guid id,
        Guid applicationId,
        Guid entityId,
        string uploaderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(
            """
            SELECT state, key, errors::text, created_at
            FROM axis.record_imports
            WHERE id = @id AND application_id = @application AND entity_id = @entity AND uploader_id = @uploader
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("entity", entityId);
        command.Parameters.AddWithValue("uploader", uploaderId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RecordImportStatus(
            id,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? new Dictionary<string, string[]>() : ReadErrors(reader.GetString(2)),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>
    /// Locks the import that the work item <paramref name="workItemId"/> checks <c>FOR UPDATE</c> and reads it
    /// with its file, or returns <see langword="null"/> when the item has no import.
    /// </summary>
    public static async Task<RecordImportJob?> LockForWorkItemAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid workItemId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            """
            SELECT id, application_id, entity_id, uploader_id, uploader_name, uploader_roles, state, format, file
            FROM axis.record_imports
            WHERE work_item_id = @item
            FOR UPDATE
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("item", workItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RecordImportJob(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetFieldValue<string[]>(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<byte[]>(8));
    }

    /// <summary>
    /// Ends the dry run of the import <paramref name="id"/> in <paramref name="state"/>, <c>checked</c> or
    /// <c>failed</c>, with its <paramref name="errors"/>. A failed import's file is cleared.
    /// </summary>
    public static async Task FinishCheckAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid id,
        string state,
        IReadOnlyDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(errors);

        if (state is not (RecordImportStates.Checked or RecordImportStates.Failed))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A dry run ends checked or failed.");
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.record_imports
            SET state = @state,
                errors = @errors,
                file = CASE WHEN @state = 'failed' THEN NULL ELSE file END
            WHERE id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add(new NpgsqlParameter("errors", NpgsqlDbType.Jsonb) { Value = errors.Count == 0 ? DBNull.Value : WriteErrors(errors) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string WriteErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (pointer, messages) in errors)
            {
                writer.WriteStartArray(pointer);
                foreach (var message in messages)
                {
                    writer.WriteStringValue(message);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    // jsonb keeps no key order, so the errors are put back in the order of the rows they name. The columns
    // and the file as a whole come first, and the sort is stable.
    private static Dictionary<string, string[]> ReadErrors(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .OrderBy(property => RowNumber(property.Name))
            .ToDictionary(
                property => property.Name,
                property => property.Value.EnumerateArray().Select(message => message.GetString()!).ToArray(),
                StringComparer.Ordinal);
    }

    private static int RowNumber(string pointer)
    {
        const string prefix = "/rows/";
        if (!pointer.StartsWith(prefix, StringComparison.Ordinal))
        {
            return -1;
        }

        var end = pointer.IndexOf('/', prefix.Length);
        var number = end < 0 ? pointer[prefix.Length..] : pointer[prefix.Length..end];
        return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var row) ? row : -1;
    }
}
