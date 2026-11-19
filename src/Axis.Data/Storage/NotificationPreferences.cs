using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Storage;

/// <summary>
/// A person's own row in <c>axis.notification_preferences</c>, see <c>docs/reference/notifications.md#notification-preferences</c>.
/// Every query names the user id, so a row of another person is never read or changed. A person who never saved reads
/// <see cref="Defaults"/>.
/// </summary>
public static class NotificationPreferences
{
    /// <summary>The sources of notices a person chooses for, in the order the API answers them.</summary>
    public static readonly string[] SourceNames = ["task", "mention", "process", "authoringRequest"];

    private const string ReadSql =
        """
        SELECT "sources"::text, "digest_hour", "time_zone" FROM "axis"."notification_preferences" WHERE "user_id" = @user
        """;

    private const string ReplaceSql =
        """
        INSERT INTO "axis"."notification_preferences" ("user_id", "sources", "digest_hour", "time_zone")
        VALUES (@user, @sources, @hour, @zone)
        ON CONFLICT ("user_id") DO UPDATE
        SET "sources" = excluded."sources", "digest_hour" = excluded."digest_hour", "time_zone" = excluded."time_zone", "updated_at" = now()
        """;

    /// <summary>
    /// SQL for an array of the sources that <c>@user</c> turned off in the application. It is empty when they saved nothing.
    /// A query that uses it binds <c>@user</c>, and the planner runs it once for the query, not for each row.
    /// </summary>
    public const string ApplicationOffSourcesSql =
        """ARRAY(SELECT s.key FROM "axis"."notification_preferences" p CROSS JOIN LATERAL jsonb_each(p."sources") s WHERE p."user_id" = @user AND s.value #>> '{channels,application}' = 'false')""";

    private const string EmailChoiceSql =
        """
        SELECT "user_id", "sources" #>> ARRAY[@source, 'channels', 'email'], "sources" #>> ARRAY[@source, 'digest'], "digest_hour", "time_zone"
        FROM "axis"."notification_preferences" WHERE "user_id" = ANY(@users)
        """;

    /// <summary>How one source reaches a person: in the application, by email, and whether the email waits for the daily digest.</summary>
    public sealed record Source(bool Application, bool Email, bool Digest);

    /// <summary>A person's choices: one <see cref="Source"/> for each of <see cref="SourceNames"/>, the digest's hour and its IANA time zone.</summary>
    public sealed record Preferences(IReadOnlyDictionary<string, Source> Sources, int DigestHour, string TimeZone);

    /// <summary>What a person who never saved has: both channels on, no digest, at 8 in the morning, UTC.</summary>
    public static Preferences Defaults { get; } = new(
        SourceNames.ToDictionary(name => name, _ => new Source(true, true, false), StringComparer.Ordinal), 8, "UTC");

    /// <summary>Reads the person's preferences, or <see cref="Defaults"/> when none are saved.</summary>
    public static async Task<Preferences> ReadAsync(NpgsqlConnection connection, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(ReadSql, connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Defaults;
        }

        using var document = JsonDocument.Parse(reader.GetString(0));
        var sources = SourceNames.ToDictionary(
            name => name,
            name => document.RootElement.TryGetProperty(name, out var source) ? ReadSource(source) : Defaults.Sources[name],
            StringComparer.Ordinal);
        return new Preferences(sources, reader.GetInt16(1), reader.GetString(2));
    }

    /// <summary>Saves the preferences as the person's only row, replacing any saved before.</summary>
    public static async Task ReplaceAsync(NpgsqlConnection connection, string userId, Preferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(preferences);

        await using var command = new NpgsqlCommand(ReplaceSql, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("sources", NpgsqlDbType.Jsonb) { Value = WriteSources(preferences.Sources) });
        command.Parameters.Add(new NpgsqlParameter("hour", NpgsqlDbType.Smallint) { Value = (short)preferences.DigestHour });
        command.Parameters.AddWithValue("zone", preferences.TimeZone);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// How the email channel of one source reaches a person: whether it is on, whether it waits for the daily digest, and
    /// the digest's hour and IANA time zone.
    /// </summary>
    public sealed record EmailChoice(bool Email, bool Digest, int DigestHour, string TimeZone);

    /// <summary>
    /// The email choice of <paramref name="source"/> for each person among <paramref name="userIds"/> who saved a row.
    /// A person who saved nothing is not in the answer and has the defaults. A source missing from a row also counts as
    /// the defaults. It is empty, with no query, when no ids are given.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, EmailChoice>> FindEmailChoicesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string source,
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(userIds);

        var choices = new Dictionary<string, EmailChoice>(StringComparer.Ordinal);
        if (userIds.Count == 0)
        {
            return choices;
        }

        await using var command = new NpgsqlCommand(EmailChoiceSql, connection, transaction);
        command.Parameters.Add(new NpgsqlParameter("users", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = userIds.ToArray() });
        command.Parameters.AddWithValue("source", source);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            choices[reader.GetString(0)] = new EmailChoice(
                reader.IsDBNull(1) || reader.GetString(1) != "false",
                !reader.IsDBNull(2) && reader.GetString(2) == "true",
                reader.GetInt16(3),
                reader.GetString(4));
        }

        return choices;
    }

    // The stored shape is the API body's: { "task": { "channels": { "application": true, "email": true }, "digest": false }, ... }.
    private static string WriteSources(IReadOnlyDictionary<string, Source> sources)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var name in SourceNames)
            {
                var source = sources[name];
                writer.WriteStartObject(name);
                writer.WriteStartObject("channels");
                writer.WriteBoolean("application", source.Application);
                writer.WriteBoolean("email", source.Email);
                writer.WriteEndObject();
                writer.WriteBoolean("digest", source.Digest);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Source ReadSource(JsonElement source)
    {
        var channels = source.GetProperty("channels");
        return new Source(
            channels.GetProperty("application").GetBoolean(),
            channels.GetProperty("email").GetBoolean(),
            source.GetProperty("digest").GetBoolean());
    }
}
