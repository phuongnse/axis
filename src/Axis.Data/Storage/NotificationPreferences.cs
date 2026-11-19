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
