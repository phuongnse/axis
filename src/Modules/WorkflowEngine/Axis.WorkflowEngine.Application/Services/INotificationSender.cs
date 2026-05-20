namespace Axis.WorkflowEngine.Application.Services;

/// <summary>
/// Sends email or webhook notifications for a Notification step. Implemented in Infrastructure.
/// Fire-and-forget: the engine continues regardless of delivery outcome.
/// </summary>
public interface INotificationSender
{
    /// <summary>
    /// Dispatches the notification configured in <paramref name="stepConfig"/>,
    /// interpolating <paramref name="context"/> values into recipient/subject/body templates.
    /// </summary>
    /// <returns>
    /// A delivery result: <c>{ status: "sent" | "failed", timestamp }</c>.
    /// Never throws — failures are captured in the return value.
    /// </returns>
    Task<IReadOnlyDictionary<string, object?>> SendAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default);
}
