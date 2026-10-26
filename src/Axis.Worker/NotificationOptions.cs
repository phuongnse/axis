namespace Axis.Worker;

/// <summary>The in-app notification settings the worker reads, from the <c>Notifications</c> configuration section.</summary>
internal sealed class NotificationOptions
{
    /// <summary>How many days an in-app notification is kept. It is at least 1.</summary>
    public required int RetentionDays { get; init; }
}
