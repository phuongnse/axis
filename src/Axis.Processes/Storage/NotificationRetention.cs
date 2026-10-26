using Npgsql;

namespace Axis.Processes.Storage;

/// <summary>
/// The retention rule of in-app notifications, which the worker applies: a notification is kept for
/// <c>Notifications:RetentionDays</c> after its <c>created_at</c>.
/// </summary>
public static class NotificationRetention
{
    /// <summary>How often the worker deletes the old notifications of a tenant.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// Deletes the notifications created before <paramref name="retentionDays"/> days before <paramref name="now"/>,
    /// and their <c>axis.notification_reads</c> rows, in one transaction. It returns how many notification rows it deleted.
    /// A notification created exactly at the limit stays.
    /// </summary>
    public static async Task<int> DeleteExpiredAsync(NpgsqlConnection connection, DateTimeOffset now, int retentionDays, CancellationToken cancellationToken = default)
    {
        // A period that reaches back past the first date of the calendar leaves nothing to delete.
        if (retentionDays >= (now - DateTimeOffset.MinValue).TotalDays)
        {
            return 0;
        }

        var before = now.AddDays(-retentionDays);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var reads = new NpgsqlCommand(
            "DELETE FROM axis.notification_reads WHERE notification_id IN (SELECT id FROM axis.notifications WHERE created_at < @before)",
            connection,
            transaction))
        {
            reads.Parameters.AddWithValue("before", before);
            await reads.ExecuteNonQueryAsync(cancellationToken);
        }

        int deleted;
        await using (var notifications = new NpgsqlCommand("DELETE FROM axis.notifications WHERE created_at < @before", connection, transaction))
        {
            notifications.Parameters.AddWithValue("before", before);
            deleted = await notifications.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }
}
