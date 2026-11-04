using Axis.ControlPlane.Telemetry;
using Npgsql;

namespace Axis.ControlPlane.Jobs;

/// <summary>
/// Runs the durable jobs of the control plane until it is stopped, one at a time. It claims the oldest due job of a kind that it
/// has a handler for, under a lease that it renews every third of its length while the handler runs. A job whose runner stopped
/// is claimed again once its lease ends. A claim counts an attempt, so a job that keeps stopping its runner still ends <c>failed</c>.
/// A handler that throws gets the job queued again after a delay that doubles, up to <see cref="JobRunnerOptions.MaxDelay"/>, and after
/// <see cref="JobRunnerOptions.MaxAttempts"/> attempts the job is <c>failed</c>, an error log names it and
/// <see cref="ControlPlaneMetrics.JobsFailed"/> counts it. The attempt count fences every final update, so a runner that lost its claim
/// changes nothing. With no handler the runner is idle and sends no query. It writes no audit record: handlers do, in their own transactions.
/// </summary>
internal sealed partial class JobRunner : BackgroundService
{
    // The most that last_error keeps of an exception: its type and the start of its message.
    private const int MaxErrorLength = 500;

    private readonly NpgsqlDataSource _dataSource;
    private readonly Dictionary<string, IJobHandler> _handlers;
    private readonly JobRunnerOptions _options;
    private readonly ILogger<JobRunner> _logger;
    private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    public JobRunner(NpgsqlDataSource dataSource, IEnumerable<IJobHandler> handlers, JobRunnerOptions options, ILogger<JobRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _dataSource = dataSource;
        _handlers = handlers.ToDictionary(handler => handler.Kind, StringComparer.Ordinal);
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var ran = false;
            try
            {
                ran = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // The job stays running under its lease and is claimed again when the lease ends.
                LogLoopFailed(exception);
            }

            if (!ran)
            {
                try
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Claims one due job and runs it. It returns <see langword="false"/> when no job was claimed.</summary>
    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_handlers.Count == 0)
        {
            return false;
        }

        ClaimedJob? job;
        await using (var connection = await _dataSource.OpenConnectionAsync(cancellationToken))
        {
            job = await JobQueue.ClaimAsync(connection, _owner, [.. _handlers.Keys], _options.LeaseDuration, cancellationToken);
        }

        if (job is null)
        {
            return false;
        }

        // A claim past the limit is a lease that ended on the last attempt: the runner stopped, and the handler does not run again.
        if (job.Attempt > _options.MaxAttempts)
        {
            await FailAsync(job, "The runner stopped while running the job.", job.Attempt - 1, cancellationToken);
            return true;
        }

        Exception? failure = null;
        using var renewing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var renewal = RenewAsync(job, renewing.Token);
        try
        {
            await _handlers[job.Kind].RunAsync(job, cancellationToken);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            failure = exception;
        }
        finally
        {
            // The renewal stops before the final update, so it cannot extend a lease that the update has just ended.
            await renewing.CancelAsync();
            try
            {
                await renewal;
            }
            catch (OperationCanceledException)
            {
                // The renewal ends with the run.
            }
        }

        // A stop in the middle of a run leaves the job running. Its lease ends and the next runner claims it.
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is null)
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await JobQueue.SucceedAsync(connection, job, cancellationToken);
            return true;
        }

        var error = Describe(failure);
        if (job.Attempt >= _options.MaxAttempts)
        {
            await FailAsync(job, error, job.Attempt, cancellationToken);
        }
        else
        {
            var delay = _options.RetryDelay(job.Attempt);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            if (await JobQueue.RetryAsync(connection, job, delay, error, cancellationToken))
            {
                LogRetry(job.Id, job.Kind, job.Attempt, delay);
            }
        }

        return true;
    }

    private async Task FailAsync(ClaimedJob job, string error, int attempts, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        if (await JobQueue.FailAsync(connection, job, error, cancellationToken))
        {
            LogFailed(job.Id, job.Kind, attempts);
            ControlPlaneMetrics.RecordJobFailed(job.Kind);
        }
    }

    // Renews the lease every third of its length on a connection of its own. It stops when no row changes: the job ended,
    // or another runner claimed it again.
    private async Task RenewAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_options.LeaseDuration / 3);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
                if (!await JobQueue.RenewAsync(connection, job, _options.LeaseDuration, cancellationToken))
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The next tick tries again, on a new connection.
                LogRenewFailed(exception, job.Id, job.Kind);
            }
        }
    }

    // The type and the start of the message, never the payload.
    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().FullName}: {exception.Message}";
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }

    [LoggerMessage(LogLevel.Warning, "The job runner could not claim or finish a job. It tries again.")]
    private partial void LogLoopFailed(Exception exception);

    [LoggerMessage(LogLevel.Warning, "Job {JobId} of kind {JobKind} failed on attempt {Attempt} and runs again in {Delay}.")]
    private partial void LogRetry(Guid jobId, string jobKind, int attempt, TimeSpan delay);

    [LoggerMessage(LogLevel.Error, "Job {JobId} of kind {JobKind} failed after {Attempts} attempts.")]
    private partial void LogFailed(Guid jobId, string jobKind, int attempts);

    [LoggerMessage(LogLevel.Warning, "The lease of job {JobId} of kind {JobKind} could not be renewed.")]
    private partial void LogRenewFailed(Exception exception, Guid jobId, string jobKind);
}
