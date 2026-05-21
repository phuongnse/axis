using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.Shared.Infrastructure.Wolverine;

public sealed class HandlerLoggingMiddleware(ILogger<HandlerLoggingMiddleware> logger)
{
    /// <summary>
    /// Wolverine generates a try/finally and injects the Exception if one was thrown; null on success.
    /// Envelope is re-resolved per-invocation so there is no shared mutable state.
    /// </summary>
    public void Finally(Envelope envelope, Exception? exception)
    {
        if (exception is not null)
            logger.LogError(exception,
                "Unhandled exception in handler for {MessageType}",
                envelope.Message?.GetType().Name ?? envelope.MessageType ?? "Unknown");
    }
}
