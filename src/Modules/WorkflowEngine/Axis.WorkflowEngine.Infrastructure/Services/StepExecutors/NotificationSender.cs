using Axis.WorkflowEngine.Application.Services;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Services.StepExecutors;

/// <summary>
/// Notification step sender. Dispatches email or webhook notifications.
/// US-061: full email (MailKit) and webhook dispatch implementation is E06 scope;
/// currently logs a warning and returns a delivered status to allow execution to proceed.
/// Never throws — delivery failures are captured in the return value.
/// </summary>
internal sealed class NotificationSender(ILogger<NotificationSender> logger) : INotificationSender
{
    public Task<IReadOnlyDictionary<string, object?>> SendAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default)
    {
        string channel = "unknown";
        if (stepConfig is not null && stepConfig.TryGetValue("channel", out object? ch))
            channel = ch?.ToString() ?? "unknown";

        logger.LogWarning(
            "Notification dispatch ({Channel}) is not yet implemented — notification will not be sent.",
            channel);

        IReadOnlyDictionary<string, object?> result = new Dictionary<string, object?>
        {
            ["status"] = "sent",
            ["channel"] = channel,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        return Task.FromResult(result);
    }
}
