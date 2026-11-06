using System.Text.Json.Nodes;
using Npgsql;

namespace Axis.ControlPlane.Jobs;

/// <summary>
/// Queues the recurring jobs of the control plane until it is stopped. For each <see cref="RecurringJob"/> it adds one job to
/// the queue at most once for each interval, and the <see cref="JobRunner"/> runs it under its lease and retries. Every replica
/// runs this schedule, so a transaction lock on the kind and a check keep it to one job: none is queued when a job of the kind is
/// queued or running, or was created within the interval. With no entry the schedule is idle and sends no query.
/// </summary>
internal sealed partial class JobSchedule : BackgroundService
{
    // How often the schedule looks at the queue. The intervals of the entries decide how often a job is queued.
    private static readonly TimeSpan _tick = TimeSpan.FromSeconds(30);

    private readonly NpgsqlDataSource _dataSource;
    private readonly RecurringJob[] _entries;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobSchedule> _logger;

    public JobSchedule(NpgsqlDataSource dataSource, IEnumerable<RecurringJob> entries, TimeProvider clock, ILogger<JobSchedule> logger)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _dataSource = dataSource;
        _entries = [.. entries];
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_entries.Length == 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(_tick, _clock);
        do
        {
            try
            {
                await EnqueueDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The next tick tries again.
                LogEnqueueFailed(exception);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>Queues a job of each entry that is due and returns how many it queued.</summary>
    internal async Task<int> EnqueueDueAsync(CancellationToken cancellationToken)
    {
        var queued = 0;
        foreach (var entry in _entries)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var @lock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('control_plane.jobs:' || @kind))", connection, transaction))
            {
                @lock.Parameters.AddWithValue("kind", entry.Kind);
                await @lock.ExecuteNonQueryAsync(cancellationToken);
            }

            bool exists;
            await using (var check = new NpgsqlCommand(
                """
                SELECT EXISTS (
                    SELECT 1 FROM control_plane.jobs
                    WHERE kind = @kind AND (state IN ('queued', 'running') OR created_at > clock_timestamp() - @interval))
                """,
                connection,
                transaction))
            {
                check.Parameters.AddWithValue("kind", entry.Kind);
                check.Parameters.AddWithValue("interval", entry.Interval);
                exists = (bool)(await check.ExecuteScalarAsync(cancellationToken))!;
            }

            if (!exists)
            {
                await JobQueue.EnqueueAsync(connection, transaction, entry.Kind, new JsonObject(), null, cancellationToken);
                queued++;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return queued;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The job schedule could not queue its jobs. It tries again.")]
    private partial void LogEnqueueFailed(Exception exception);
}
