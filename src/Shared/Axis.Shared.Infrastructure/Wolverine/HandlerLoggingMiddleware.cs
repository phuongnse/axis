using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.Shared.Infrastructure.Wolverine;

public sealed class HandlerLoggingMiddleware(ILogger<HandlerLoggingMiddleware> logger)
{
    public void OnException(Exception exception, Envelope envelope)
    {
        logger.LogError(
            exception,
            "Unhandled exception in handler for {MessageType}",
            envelope.Message?.GetType().Name ?? envelope.MessageType ?? "Unknown");
    }
}
