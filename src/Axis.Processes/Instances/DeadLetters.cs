using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Lists the dead letters of a process, or of the whole tenant, from <c>axis.dead_letters</c> over a connection to a tenant
/// database. A dead letter holds ids and a fixed error text, never a record value.
/// </summary>
public static class DeadLetters
{
    /// <summary>The state of a dead letter that waits for an operator.</summary>
    public const string Open = "open";

    /// <summary>The state of a dead letter that an operator queued again.</summary>
    public const string Redelivered = "redelivered";

    /// <summary>The state of a dead letter that an operator closed.</summary>
    public const string Discarded = "discarded";

    /// <summary>The states a dead letter has.</summary>
    public static IReadOnlyList<string> States { get; } = [Open, Redelivered, Discarded];

    internal const string Columns =
        "id, kind, event_id, trigger_id, process_instance_id, email_id, occurrence_at, attempts, last_error, state, reason, settled_by, created_at, settled_at, webhook_id, application_id, process_id";

    /// <summary>
    /// Reads one page of the dead letters of the process, newest first, and the number of them on every
    /// page. <paramref name="state"/> keeps one state, or every state when it is <see langword="null"/>.
    /// </summary>
    public static async Task<DeadLetterPage> ListAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid processId,
        string? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        const string where = "WHERE application_id = @application AND process_id = @process AND (@state::text IS NULL OR state = @state)";

        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.dead_letters {where}", connection))
        {
            AddFilter(count, applicationId, processId, state);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM axis.dead_letters {where} ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset",
            connection);
        AddFilter(command, applicationId, processId, state);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<DeadLetterRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        return new DeadLetterPage(items, totalCount);
    }

    /// <summary>
    /// Reads one page of the dead letters of every application of the tenant database, newest first, and the
    /// number of them on every page, including those that belong to no process. <paramref name="applicationId"/>
    /// keeps one application and <paramref name="state"/> keeps one state, or every one when they are
    /// <see langword="null"/>.
    /// </summary>
    public static async Task<DeadLetterPage> ListTenantAsync(
        NpgsqlConnection connection,
        Guid? applicationId,
        string? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        const string where = "WHERE (@application::uuid IS NULL OR application_id = @application) AND (@state::text IS NULL OR state = @state)";

        long totalCount;
        await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.dead_letters {where}", connection))
        {
            AddTenantFilter(count, applicationId, state);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM axis.dead_letters {where} ORDER BY created_at DESC, id DESC LIMIT @limit OFFSET @offset",
            connection);
        AddTenantFilter(command, applicationId, state);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<DeadLetterRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }

        return new DeadLetterPage(items, totalCount);
    }

    internal static DeadLetterRow Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            reader.IsDBNull(14) ? null : reader.GetGuid(14),
            reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16));

    private static void AddTenantFilter(NpgsqlCommand command, Guid? applicationId, string? state)
    {
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = applicationId is null ? DBNull.Value : applicationId });
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlTypes.NpgsqlDbType.Text) { Value = state is null ? DBNull.Value : state });
    }

    private static void AddFilter(NpgsqlCommand command, Guid applicationId, Guid processId, string? state)
    {
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlTypes.NpgsqlDbType.Text) { Value = state is null ? DBNull.Value : state });
    }
}

/// <summary>One row of <c>axis.dead_letters</c>.</summary>
public sealed record DeadLetterRow(
    Guid Id,
    string Kind,
    Guid? EventId,
    Guid? TriggerId,
    Guid? ProcessInstanceId,
    Guid? EmailId,
    DateTimeOffset? OccurrenceAt,
    int Attempts,
    string LastError,
    string State,
    string? Reason,
    string? SettledBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SettledAt,
    Guid? WebhookId,
    Guid ApplicationId,
    Guid? ProcessId);

/// <summary>One page of dead letters, and the number of them on every page.</summary>
public sealed record DeadLetterPage(IReadOnlyList<DeadLetterRow> Items, long TotalCount);
