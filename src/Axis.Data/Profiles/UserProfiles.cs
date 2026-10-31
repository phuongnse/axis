using System.Collections.Frozen;
using Axis.Data.Audit;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Profiles;

/// <summary>
/// Writes and reads <c>axis.user_profiles</c> of the tenant database. Other modules reach a user profile only through
/// this class, never through the table.
/// </summary>
public static class UserProfiles
{
    private const string ExistsSql = """SELECT EXISTS (SELECT 1 FROM "axis"."user_profiles" WHERE "user_id" = @user)""";

    private const string LocaleSql = """SELECT "locale" FROM "axis"."user_profiles" WHERE "user_id" = @user""";

    private const string ContactsSql = """SELECT "user_id", "email", "locale" FROM "axis"."user_profiles" WHERE "user_id" = ANY(@users)""";

    private const string DisplayNamesSql = """SELECT "user_id", "display_name" FROM "axis"."user_profiles" WHERE "user_id" = ANY(@users)""";

    // Actors that are not users. A profile with one of these ids never names them.
    private static readonly FrozenSet<string> _reservedActors =
        new[] { AuditActors.System, AuditActors.Anonymous, "startup", "operator", "unknown" }.ToFrozenSet(StringComparer.Ordinal);

    private const string LastSignInsSql = """SELECT "user_id", "last_signed_in_at" FROM "axis"."user_profiles" WHERE "user_id" = ANY(@users)""";

    private const string SearchSql =
        """
        SELECT "user_id", "display_name" FROM "axis"."user_profiles"
        WHERE position(lower(@search) in lower("display_name")) > 0
        ORDER BY "display_name", "user_id"
        LIMIT @limit
        """;

    private const string UpsertSql =
        """
        INSERT INTO "axis"."user_profiles"
            ("user_id", "display_name", "email", "locale", "first_signed_in_at", "last_signed_in_at")
        VALUES (@user, @displayName, @email, @locale, now(), now())
        ON CONFLICT ("user_id") DO UPDATE SET
            "display_name" = EXCLUDED."display_name",
            "email" = EXCLUDED."email",
            "locale" = EXCLUDED."locale",
            "last_signed_in_at" = EXCLUDED."last_signed_in_at"
        """;

    /// <summary>
    /// Saves the profile of a user who has just signed in. A first sign-in adds the profile. A later one replaces the
    /// display name, email and locale, sets the last sign-in time and keeps the first one.
    /// </summary>
    /// <param name="email">The email, or null when the ID token had none.</param>
    /// <param name="locale">The locale, or null when the ID token had none.</param>
    public static async Task UpsertAsync(
        NpgsqlConnection connection,
        string userId,
        string displayName,
        string? email,
        string? locale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(displayName);

        await using var command = new NpgsqlCommand(UpsertSql, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("displayName", displayName);
        command.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Text) { Value = email is { } mail ? mail : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("locale", NpgsqlDbType.Text) { Value = locale is { } tag ? tag : DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Whether the user has a profile, which is so once the user has signed in.</summary>
    /// <param name="transaction">The transaction the read joins, or null.</param>
    public static async Task<bool> ExistsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var command = new NpgsqlCommand(ExistsSql, connection, transaction);
        command.Parameters.AddWithValue("user", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The locale of the user's profile, or null when the user has no profile or the profile has no locale.</summary>
    public static async Task<string?> FindLocaleAsync(NpgsqlConnection connection, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var command = new NpgsqlCommand(LocaleSql, connection);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>
    /// The email and locale of the profiles of <paramref name="userIds"/>, by user id. A user with no profile is not in the result.
    /// </summary>
    /// <param name="transaction">The transaction the read joins, or null.</param>
    public static async Task<IReadOnlyDictionary<string, UserContact>> FindContactsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userIds);

        var contacts = new Dictionary<string, UserContact>(StringComparer.Ordinal);
        if (userIds.Count == 0)
        {
            return contacts;
        }

        await using var command = new NpgsqlCommand(ContactsSql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("users", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            contacts[reader.GetString(0)] = new UserContact(
                await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
                await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2));
        }

        return contacts;
    }

    /// <summary>
    /// The display names of the profiles of <paramref name="userIds"/>, by user id. A user with no profile and a reserved
    /// actor (<c>system</c>, <c>anonymous</c>, <c>startup</c>, <c>operator</c>, <c>unknown</c>) are not in the result.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> FindDisplayNamesAsync(
        NpgsqlConnection connection, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userIds);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var users = userIds.Where(id => !_reservedActors.Contains(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (users.Length == 0)
        {
            return names;
        }

        await using var command = new NpgsqlCommand(DisplayNamesSql, connection);
        command.Parameters.Add(new NpgsqlParameter("users", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = users });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names[reader.GetString(0)] = reader.GetString(1);
        }

        return names;
    }

    /// <summary>
    /// The last sign-in time of the profiles of <paramref name="userIds"/>, by user id. A user with no profile is not in the result.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, DateTimeOffset>> FindLastSignInsAsync(
        NpgsqlConnection connection, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userIds);

        var signIns = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (userIds.Count == 0)
        {
            return signIns;
        }

        await using var command = new NpgsqlCommand(LastSignInsSql, connection);
        command.Parameters.Add(new NpgsqlParameter("users", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds.ToArray() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            signIns[reader.GetString(0)] = await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken);
        }

        return signIns;
    }

    /// <summary>
    /// The profiles whose display name contains <paramref name="search"/>, ignoring letter case, sorted by display name and
    /// then user id. An empty search matches every profile.
    /// </summary>
    /// <param name="limit">The most profiles to return.</param>
    public static async Task<IReadOnlyList<UserProfileSummary>> SearchAsync(
        NpgsqlConnection connection, string search, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(search);

        await using var command = new NpgsqlCommand(SearchSql, connection);
        command.Parameters.AddWithValue("search", search);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new List<UserProfileSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new UserProfileSummary(reader.GetString(0), reader.GetString(1)));
        }

        return profiles;
    }
}

/// <summary>What a user picker shows of a profile.</summary>
/// <param name="Id">The user id, the subject of the ID token.</param>
/// <param name="DisplayName">The name shown for the user.</param>
public sealed record UserProfileSummary(string Id, string DisplayName);

/// <summary>How to reach a user by email.</summary>
/// <param name="Email">The profile's email, or null when the profile has none.</param>
/// <param name="Locale">The profile's locale, or null when the profile has none.</param>
public sealed record UserContact(string? Email, string? Locale);
