using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Studio;
using Npgsql;

namespace Axis.Server.Studio;

/// <summary>
/// Records who wrote each file of an application folder in Studio, with the version the write left, in
/// <c>axis.resource_writes</c>, and reads those rows back for <c>GET .../resource-activity</c>, see
/// <c>docs/reference/studio-resource-form.md#resource-activity</c>. A record is made after the file is written, and a
/// failed record is logged and never fails the write. A folder whose manifest has no UUID <c>id</c> records nothing.
/// </summary>
internal static partial class StudioWriteRecords
{
    /// <summary>One recorded write: the version it left, the writer and the time.</summary>
    public sealed record Write(string Version, string UserId, string UserName, DateTimeOffset WrittenAt);

    /// <summary>
    /// Records the current user as the last writer of each of <paramref name="files"/>, each a path of the folder with the
    /// version the write left. It does nothing for an empty list.
    /// </summary>
    public static async Task RecordAsync(
        HttpContext httpContext, string folder, IReadOnlyList<(string File, string Version)> files)
    {
        if (files.Count == 0 || StudioFolder.ReadManifest(folder)?.Id is not { } applicationId)
        {
            return;
        }

        try
        {
            if (await CurrentUsers.FindAsync(httpContext) is not { } user)
            {
                return;
            }

            var cancellationToken = httpContext.RequestAborted;
            var connection = await httpContext.RequestServices.GetRequiredService<TenantDatabase>().GetConnectionAsync(cancellationToken);
            foreach (var (file, version) in files)
            {
                await using var upsert = new NpgsqlCommand(
                    """
                    INSERT INTO axis.resource_writes (application_id, file, version, user_id, user_name, written_at)
                    VALUES (@application, @file, @version, @userId, @userName, now())
                    ON CONFLICT (application_id, file) DO UPDATE SET
                        version = excluded.version, user_id = excluded.user_id, user_name = excluded.user_name, written_at = excluded.written_at
                    """,
                    connection);
                upsert.Parameters.AddWithValue("application", applicationId);
                upsert.Parameters.AddWithValue("file", file);
                upsert.Parameters.AddWithValue("version", version);
                upsert.Parameters.AddWithValue("userId", user.Id);
                upsert.Parameters.AddWithValue("userName", user.DisplayName);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (NpgsqlException exception)
        {
            // A list that shows a dash is the worst case, so the write that already happened stands.
            LogRecordFailed(
                httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Axis.Server.Studio.StudioWriteRecords"),
                exception);
        }
    }

    /// <summary>The recorded writes of an application, by file.</summary>
    public static async Task<IReadOnlyDictionary<string, Write>> ReadAsync(
        NpgsqlConnection connection, Guid applicationId, CancellationToken cancellationToken)
    {
        var writes = new Dictionary<string, Write>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "SELECT file, version, user_id, user_name, written_at FROM axis.resource_writes WHERE application_id = @application",
            connection);
        command.Parameters.AddWithValue("application", applicationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            writes[reader.GetString(0)] = new Write(
                reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4));
        }

        return writes;
    }

    [LoggerMessage(LogLevel.Warning, "The writer of a Studio write could not be recorded.")]
    private static partial void LogRecordFailed(ILogger logger, Exception exception);
}
