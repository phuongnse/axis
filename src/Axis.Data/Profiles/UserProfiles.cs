using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Profiles;

/// <summary>
/// Writes <c>axis.user_profiles</c> of the tenant database. Other modules write a user profile only through this
/// class, never through the table.
/// </summary>
public static class UserProfiles
{
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
}
