using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the in-app notifications of an application, and the tenant notices for administrators, over a connection to a
/// tenant database, and marks them read. A user sees the notifications addressed to their id, and those addressed to a role they hold. The caller gives the
/// roles the active release declares, so a role it no longer declares shows nothing. A notification addressed to a user
/// both ways is one notification. A tenant notice has no application. The user sees the ones addressed to a platform role
/// kind the caller says the user holds. Read state is kept per user. Ids and role names match exactly, including letter case.
/// </summary>
public static class NotificationQueries
{
    private const string VisibleWhere =
        $"""
        n.application_id = @application
          AND ((n.recipient_kind = '{ProcessNotifications.UserRecipient}' AND n.recipient = @user)
            OR (n.recipient_kind = '{ProcessNotifications.RoleRecipient}' AND n.recipient = ANY(@roles)))
        """;

    // A tenant notice has no application and goes to a platform role kind that the caller says the user holds.
    private const string TenantVisibleWhere =
        $"""
        n.application_id IS NULL
          AND n.recipient_kind = '{TenantNotices.PlatformRoleRecipient}' AND n.recipient = ANY(@roles)
        """;

    // One row for each notification, though a user and a role recipient of the same notification are two stored rows.
    private const string VisibleCteStart =
        """
        WITH visible AS (
            SELECT DISTINCT ON (n.id)
                n.id, n.release_id, n.subject_key, n.body_key, n."values"::text AS "values", n.value_types::text AS value_types, n.open_page,
                n.open_parameters::text AS open_parameters, n.created_at,
                EXISTS (SELECT 1 FROM axis.notification_reads r WHERE r.notification_id = n.id AND r.user_id = @user) AS is_read
            FROM axis.notifications n
            WHERE
        """;

    private const string VisibleCteEnd =
        """

            ORDER BY n.id
        )
        """;

    private const string VisibleCte = $"{VisibleCteStart} {VisibleWhere}{VisibleCteEnd}";

    /// <summary>Counts the notifications a user sees and the unread ones among them.</summary>
    internal const string CountsSql =
        $"""
        {VisibleCte}
        SELECT count(*) FILTER (WHERE NOT @unread OR NOT is_read), count(*) FILTER (WHERE NOT is_read) FROM visible
        """;

    private static readonly Statements _application = new(VisibleWhere);

    private static readonly Statements _tenant = new(TenantVisibleWhere);

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> notifications of the application that
    /// <paramref name="userId"/> sees, newest first and then by id, and counts them. With <paramref name="unreadOnly"/>
    /// the page and <see cref="NotificationPage.TotalCount"/> have only the unread ones.
    /// <see cref="NotificationPage.UnreadCount"/> counts every unread one. A page past the last one has no items.
    /// </summary>
    public static Task<NotificationPage> ListAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return ListAsync(
            _application, connection, command => AddVisibleParameters(command, applicationId, userId, roles), userId, unreadOnly, page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Reads the tenant notices that <paramref name="userId"/> sees, like <see cref="ListAsync"/> does for an application.
    /// A tenant notice has no application. <paramref name="platformRoles"/> are the platform role kinds the user holds, and
    /// the user sees the notices addressed to one of them.
    /// </summary>
    public static Task<NotificationPage> ListTenantNoticesAsync(
        NpgsqlConnection connection,
        string userId,
        IReadOnlyCollection<string> platformRoles,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformRoles);
        return ListAsync(
            _tenant, connection, command => AddTenantParameters(command, userId, platformRoles), userId, unreadOnly, page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Marks the notification <paramref name="id"/> read for <paramref name="userId"/>. Returns false, and writes
    /// nothing, when the user does not see a notification with that id in the application. Marking one that is
    /// already read changes nothing and returns true.
    /// </summary>
    public static Task<bool> MarkReadAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return MarkReadAsync(_application, connection, command => AddVisibleParameters(command, applicationId, userId, roles), userId, id, cancellationToken);
    }

    /// <summary>
    /// Marks the tenant notice <paramref name="id"/> read for <paramref name="userId"/>. Returns false, and writes nothing,
    /// when the user does not see a tenant notice with that id. Marking one that is already read changes nothing and returns true.
    /// </summary>
    public static Task<bool> MarkTenantNoticeReadAsync(
        NpgsqlConnection connection,
        string userId,
        IReadOnlyCollection<string> platformRoles,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformRoles);
        return MarkReadAsync(_tenant, connection, command => AddTenantParameters(command, userId, platformRoles), userId, id, cancellationToken);
    }

    /// <summary>
    /// Marks every notification of the application that <paramref name="userId"/> sees now read for that user. A
    /// notification the user does not see gets no read row.
    /// </summary>
    public static Task MarkAllReadAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        string userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return MarkAllReadAsync(_application, connection, command => AddVisibleParameters(command, applicationId, userId, roles), userId, cancellationToken);
    }

    /// <summary>Marks every tenant notice that <paramref name="userId"/> sees now read for that user. A notice the user does not see gets no read row.</summary>
    public static Task MarkAllTenantNoticesReadAsync(
        NpgsqlConnection connection,
        string userId,
        IReadOnlyCollection<string> platformRoles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformRoles);
        return MarkAllReadAsync(_tenant, connection, command => AddTenantParameters(command, userId, platformRoles), userId, cancellationToken);
    }

    private static async Task<NotificationPage> ListAsync(
        Statements statements,
        NpgsqlConnection connection,
        Action<NpgsqlCommand> addVisibleParameters,
        string userId,
        bool unreadOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        long totalCount;
        long unreadCount;
        await using (var counts = new NpgsqlCommand(statements.Counts, connection))
        {
            addVisibleParameters(counts);
            counts.Parameters.AddWithValue("unread", unreadOnly);
            await using var countReader = await counts.ExecuteReaderAsync(cancellationToken);
            await countReader.ReadAsync(cancellationToken);
            totalCount = countReader.GetInt64(0);
            unreadCount = countReader.GetInt64(1);
        }

        await using var command = new NpgsqlCommand(statements.Page, connection);
        addVisibleParameters(command);
        command.Parameters.AddWithValue("unread", unreadOnly);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<NotificationItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new NotificationItem(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetBoolean(9)));
        }

        return new NotificationPage(items, totalCount, unreadCount);
    }

    private static async Task<bool> MarkReadAsync(
        Statements statements,
        NpgsqlConnection connection,
        Action<NpgsqlCommand> addVisibleParameters,
        string userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);

        await using var command = new NpgsqlCommand(statements.MarkRead, connection);
        addVisibleParameters(command);
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task MarkAllReadAsync(
        Statements statements,
        NpgsqlConnection connection,
        Action<NpgsqlCommand> addVisibleParameters,
        string userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userId);

        await using var command = new NpgsqlCommand(statements.MarkAll, connection);
        addVisibleParameters(command);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddVisibleParameters(NpgsqlCommand command, Guid applicationId, string userId, IReadOnlyCollection<string> roles) =>
        TaskQueries.AddMayActParameters(command, applicationId, userId, roles);

    private static void AddTenantParameters(NpgsqlCommand command, string userId, IReadOnlyCollection<string> platformRoles)
    {
        command.Parameters.Add(new NpgsqlParameter("user", NpgsqlDbType.Text) { Value = userId });
        command.Parameters.Add(new NpgsqlParameter("roles", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = platformRoles.ToArray() });
    }

    // The four statements for one rule of which rows a user sees.
    private sealed class Statements(string visibleWhere)
    {
        private readonly string _cte = $"{VisibleCteStart} {visibleWhere}{VisibleCteEnd}";

        public string Counts =>
            $"""
            {_cte}
            SELECT count(*) FILTER (WHERE NOT @unread OR NOT is_read), count(*) FILTER (WHERE NOT is_read) FROM visible
            """;

        public string Page =>
            $"""
            {_cte}
            SELECT id, release_id, subject_key, body_key, "values", value_types, open_page, open_parameters, created_at, is_read
            FROM visible
            WHERE NOT @unread OR NOT is_read
            ORDER BY created_at DESC, id DESC
            LIMIT @limit OFFSET @offset
            """;

        public string MarkRead =>
            $"""
            WITH visible AS (
                SELECT DISTINCT n.id FROM axis.notifications n WHERE n.id = @id AND {visibleWhere}
            ), marked AS (
                INSERT INTO axis.notification_reads (notification_id, user_id)
                SELECT id, @user FROM visible
                ON CONFLICT DO NOTHING
            )
            SELECT EXISTS (SELECT 1 FROM visible)
            """;

        public string MarkAll =>
            $"""
            INSERT INTO axis.notification_reads (notification_id, user_id)
            SELECT DISTINCT n.id, @user FROM axis.notifications n WHERE {visibleWhere}
            ON CONFLICT DO NOTHING
            """;
    }
}

/// <summary>One page of the notifications a user sees, with the number of them on every page and the number unread.</summary>
/// <param name="Items">The notifications of the page.</param>
/// <param name="TotalCount">The notifications the list matches: every one, or only the unread ones when it was asked for those.</param>
/// <param name="UnreadCount">Every unread notification the user sees, whatever the list matches.</param>
public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, long TotalCount, long UnreadCount);

/// <summary>A notification as stored, with the user's read state. The values and parameters are the stored JSON text.</summary>
/// <param name="Id">The notification's id.</param>
/// <param name="ReleaseId">The release whose texts the keys belong to, or null for a tenant notice, whose keys are platform texts.</param>
/// <param name="SubjectKey">The text key of the subject.</param>
/// <param name="BodyKey">The text key of the body.</param>
/// <param name="Values">The raw placeholder values, a JSON object.</param>
/// <param name="ValueTypes">The type name of each value, a JSON object keyed like <paramref name="Values"/>. It is empty for a notification stored before the types were kept.</param>
/// <param name="OpenPage">The declared name of the page to open, or null.</param>
/// <param name="OpenParameters">The raw page parameters, a JSON object, or null when there is no page.</param>
/// <param name="CreatedAt">When the notification was stored.</param>
/// <param name="Read">Whether the user has read it.</param>
public sealed record NotificationItem(
    Guid Id,
    Guid? ReleaseId,
    string SubjectKey,
    string BodyKey,
    string Values,
    string ValueTypes,
    string? OpenPage,
    string? OpenParameters,
    DateTimeOffset CreatedAt,
    bool Read);
