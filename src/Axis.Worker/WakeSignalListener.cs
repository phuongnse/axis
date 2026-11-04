using Npgsql;

namespace Axis.Worker;

/// <summary>
/// Under the directory source, holds one direct connection to the platform database that runs <c>LISTEN axis_work</c> and passes the
/// tenant ids it is sent to <see cref="WakeSignals"/>. The connection is named <c>axis-worker-listen</c> and does not use a pool,
/// because <c>LISTEN</c> keeps state on the session. When it drops, the worker connects again at once, and while connecting fails it
/// retries every <c>Worker:PollInterval</c>. Every successful connection asks for a poll of every tenant, because a signal sent
/// while the connection was down is lost.
/// </summary>
internal sealed partial class WakeSignalListener(
    string platformConnectionString,
    WakeSignals signals,
    WorkerOptions options,
    ILogger<WakeSignalListener> logger) : BackgroundService
{
    internal const string ApplicationName = "axis-worker-listen";

    private const int KeepAliveSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(platformConnectionString)
        {
            Pooling = false,
            ApplicationName = ApplicationName,
            KeepAlive = KeepAliveSeconds,
        }.ConnectionString;

        var outage = false;
        var failedLastAttempt = false;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(connectionString);
                    connection.Notification += (_, notification) => signals.Wake(notification.Payload);
                    await connection.OpenAsync(stoppingToken);
                    await using (var listen = new NpgsqlCommand("LISTEN axis_work", connection))
                    {
                        await listen.ExecuteNonQueryAsync(stoppingToken);
                    }

                    if (outage)
                    {
                        LogReconnected();
                    }
                    else
                    {
                        LogListening();
                    }

                    outage = false;
                    failedLastAttempt = false;

                    // Signals sent before LISTEN took effect, or while the connection was down, are lost.
                    signals.WakeAll();
                    while (true)
                    {
                        await connection.WaitAsync(stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // The worker must not stop: a lost signal only delays work until the next poll.
                    if (!outage)
                    {
                        outage = true;
                        LogConnectionLost(exception.GetType().Name);
                    }

                    // A drop after a good LISTEN connects again at once. A failed attempt waits, so a database that is down is not tried in a tight loop.
                    if (failedLastAttempt)
                    {
                        await Task.Delay(options.PollInterval, stoppingToken);
                    }

                    failedLastAttempt = true;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The worker stops.
        }
    }

    [LoggerMessage(LogLevel.Information, "Listening for wake signals on the platform database.")]
    private partial void LogListening();

    [LoggerMessage(LogLevel.Information, "Listening for wake signals again. The worker polls every tenant once.")]
    private partial void LogReconnected();

    [LoggerMessage(LogLevel.Warning, "The connection that listens for wake signals failed ({Cause}). The worker connects again, and retries every Worker:PollInterval while that fails.")]
    private partial void LogConnectionLost(string cause);
}
