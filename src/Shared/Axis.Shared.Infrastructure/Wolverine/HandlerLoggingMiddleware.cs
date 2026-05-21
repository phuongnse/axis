using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Axis.Shared.Infrastructure.Wolverine;

/// <summary>
/// Wolverine handler middleware that emits Debug-level traces (entry, exit, elapsed ms)
/// and Error-level logs for any unhandled exception on every message handler.
/// Business-milestone logs (step dispatched, execution completed, branch selected) remain
/// in each handler — middleware has no access to domain context (execution ID, step type).
/// Registered globally via opts.Policies.AddMiddleware&lt;HandlerLoggingMiddleware&gt;() in Program.cs.
/// </summary>
public sealed class HandlerLoggingMiddleware(ILogger<HandlerLoggingMiddleware> logger)
{
    public async Task Handle(Envelope envelope, Func<Task> next)
    {
        string messageType = envelope.Message?.GetType().Name ?? envelope.MessageType ?? "Unknown";
        Stopwatch sw = Stopwatch.StartNew();

        logger.LogDebug("Handling {MessageType}", messageType);

        try
        {
            await next();
            logger.LogDebug("Handled {MessageType} in {ElapsedMs}ms", messageType, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Handler failed for {MessageType} after {ElapsedMs}ms",
                messageType, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
