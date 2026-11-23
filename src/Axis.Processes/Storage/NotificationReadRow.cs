namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.notification_reads</c>: the mark that a user read a notification, and when they cleared it. The row for a
/// role notification is per user, so one user's read does not hide it from the others. Times use the
/// database clock.
/// </summary>
public sealed class NotificationReadRow
{
    /// <summary>The notification's id. It has no foreign key, because the notification's key has three columns.</summary>
    public required Guid NotificationId { get; init; }

    public required string UserId { get; init; }

    public DateTimeOffset ReadAt { get; init; }

    /// <summary>When the user cleared the notification, or null while it is listed, see <c>docs/reference/notifications.md#in-app-notifications</c>.</summary>
    public DateTimeOffset? ClearedAt { get; init; }
}
