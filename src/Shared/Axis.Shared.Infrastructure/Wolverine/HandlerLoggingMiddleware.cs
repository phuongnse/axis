using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.Shared.Infrastructure.Wolverine;

/// <summary>
/// Wolverine middleware that provides cross-cutting Debug-level entry/exit traces
/// and Error-level logs for unhandled exceptions on every handler.
/// Uses Before/Finally convention required by Wolverine's AddMiddleware policy.
/// Registered globally in Program.cs via opts.Policies.AddMiddleware&lt;HandlerLoggingMiddleware&gt;().
/// Wolverine creates one instance per message invocation — fields are safe for per-call state.
/// </summary>
public sealed class HandlerLoggingMiddleware(ILogger<HandlerLoggingMiddleware> logger)
{
    private readonly Stopwatch _sw = new();
    private string _messageType = string.Empty;

    public void Before(Envelope envelope)
    {
        _messageType = envelope.Message?.GetType().Name ?? envelope.MessageType ?? "Unknown";
        _sw.Restart();
        logger.LogDebug("Handling {MessageType}", _messageType);
    }

    /// <summary>
    /// Wolverine generates a try/finally and injects the Exception if one was thrown; null on success.
    /// </summary>
    public void Finally(Exception? exception)
    {
        if (exception is not null)
            logger.LogError(exception,
                "Handler failed for {MessageType} after {ElapsedMs}ms",
                _messageType, _sw.ElapsedMilliseconds);
        else
            logger.LogDebug(
                "Handled {MessageType} in {ElapsedMs}ms",
                _messageType, _sw.ElapsedMilliseconds);
    }
}
