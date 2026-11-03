using Axis.Processes.Work;
using Npgsql;

namespace Axis.Server.Http;

/// <summary>The wake signal that a request sends for the tenants it queued work for.</summary>
internal static partial class WorkSignalMiddleware
{
    private static readonly TimeSpan _notifyLimit = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Collects the tenants that the request adds a work item due now for, and once the endpoint has returned runs
    /// <c>pg_notify('axis_work', tenant id)</c> on the platform database for each, see the wake signal in
    /// <c>docs/reference/worker-scheduling.md#waking-a-tenant</c>. It sends in a <c>finally</c> block, because a signal is a hint
    /// and an extra one costs a poll. Each send is cut off after about 2 seconds, and a failure is logged and never fails the
    /// request.
    /// </summary>
    public static IApplicationBuilder UseWorkSignals(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            using var scope = WorkSignals.Collect();
            try
            {
                await next(context);
            }
            finally
            {
                await NotifyAsync(context, scope.Tenants);
            }
        });

    private static async Task NotifyAsync(HttpContext context, IReadOnlyCollection<string> tenants)
    {
        if (tenants.Count == 0)
        {
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WorkSignalMiddleware).FullName!);
        using var limit = new CancellationTokenSource(_notifyLimit);
        try
        {
            await using var connection = await context.RequestServices.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync(limit.Token);
            foreach (var tenant in tenants)
            {
                await using var command = new NpgsqlCommand("SELECT pg_notify('axis_work', @tenant)", connection);
                command.Parameters.AddWithValue("tenant", tenant);
                await command.ExecuteNonQueryAsync(limit.Token);
            }
        }
        catch (Exception)
        {
            Failed(logger, string.Join(", ", tenants));
        }
    }

    [LoggerMessage(LogLevel.Warning, "The wake signal for tenant {Tenants} could not be sent.")]
    private static partial void Failed(ILogger logger, string tenants);
}
