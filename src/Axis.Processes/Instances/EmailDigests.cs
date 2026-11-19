using Axis.Configuration.Model;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// The rules that both the staging of an email and the sending of a digest share, see
/// <c>docs/reference/notifications.md#notification-preferences</c> (Digest): when a person's next digest is due, and the
/// lock that keeps one queued item for each person.
/// </summary>
internal static class EmailDigests
{
    /// <summary>The hour and time zone of a person who saved no preferences.</summary>
    internal const int DefaultHour = 8;

    /// <summary>The time zone of a person who saved no preferences.</summary>
    internal const string DefaultTimeZone = "UTC";

    /// <summary>
    /// The next instant after <paramref name="now"/> at which it is <paramref name="hour"/> o'clock in
    /// <paramref name="timeZone"/>. It follows the daylight saving rule of schedule triggers: an hour that does not exist
    /// runs at the next valid local time, and an hour that happens twice runs once, at its first instant. A zone this
    /// machine does not know counts as UTC.
    /// </summary>
    internal static DateTimeOffset NextAt(DateTimeOffset now, int hour, string timeZone)
    {
        if (!CronSchedule.TryParse($"0 {hour} * * *", out var schedule, out var error))
        {
            throw new ArgumentOutOfRangeException(nameof(hour), hour, error);
        }

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        return schedule.NextAfter(now, zone);
    }

    /// <summary>
    /// Takes the transaction lock of <paramref name="userId"/>'s digest. Staging an email for the digest and completing a
    /// digest both take it, so the person always has one queued item while a row waits, and never two.
    /// </summary>
    internal static async Task LockAsync(NpgsqlTransaction transaction, string userId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended('axis-email-digest:' || @user, 0))",
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("user", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
