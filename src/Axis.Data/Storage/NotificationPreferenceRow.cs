namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.notification_preferences</c>: how a person chose that each source of notices reaches them, see
/// <c>docs/reference/notifications.md#notification-preferences</c>. <see cref="Sources"/> is JSON in the shape of the API body.
/// </summary>
public sealed class NotificationPreferenceRow
{
    public required string UserId { get; init; }

    public required string Sources { get; init; }

    public short DigestHour { get; init; }

    public required string TimeZone { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
