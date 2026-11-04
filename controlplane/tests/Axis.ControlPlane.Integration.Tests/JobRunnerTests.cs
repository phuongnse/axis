using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Telemetry;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The job runner against a real PostgreSQL. The tests share one container and use a kind of their own each, so a runner
/// built for a kind never sees the jobs of another test.
/// </summary>
public sealed class JobRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), TestContext.Current.CancellationToken);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Two_runners_never_run_a_job_at_once_even_when_it_outlasts_the_lease_and_each_job_runs_once()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.concurrent";
        var options = new JobRunnerOptions
        {
            LeaseDuration = TimeSpan.FromMilliseconds(600),
            PollInterval = TimeSpan.FromMilliseconds(20),
            BaseDelay = TimeSpan.FromSeconds(1),
        };
        var inFlight = new ConcurrentDictionary<Guid, int>();
        var runs = new ConcurrentDictionary<Guid, int>();
        var overlaps = new ConcurrentBag<Guid>();
        var handler = new DelegateHandler(kind, async (job, cancellationToken) =>
        {
            if (inFlight.AddOrUpdate(job.Id, 1, (_, count) => count + 1) > 1)
            {
                overlaps.Add(job.Id);
            }

            runs.AddOrUpdate(job.Id, 1, (_, count) => count + 1);
            // Longer than the lease: only the renewal keeps the other runner from claiming the job.
            await Task.Delay(options.LeaseDuration * 1.5, cancellationToken);
            inFlight.AddOrUpdate(job.Id, 0, (_, count) => count - 1);
        });
        var ids = new List<Guid>();
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        {
            for (var i = 0; i < 10; i++)
            {
                ids.Add(await JobQueue.EnqueueAsync(connection, null, kind, new JsonObject { ["n"] = i }, null, token));
            }
        }

        var first = NewRunner(options, new LogCollector(), handler);
        var second = NewRunner(options, new LogCollector(), handler);
        await first.StartAsync(token);
        await second.StartAsync(token);
        try
        {
            await WaitAsync(async () => await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{kind}' AND state <> 'succeeded'") == 0, token);
        }
        finally
        {
            await first.StopAsync(CancellationToken.None);
            await second.StopAsync(CancellationToken.None);
        }

        Assert.Empty(overlaps);
        Assert.Equal(ids.Order(), runs.Keys.Order());
        Assert.All(runs.Values, count => Assert.Equal(1, count));
        Assert.Equal(10, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{kind}' AND attempts = 1"));
    }

    [Fact]
    public async Task A_job_whose_runner_stopped_is_claimed_again_only_after_its_lease_ends()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.lease";
        var options = new JobRunnerOptions { LeaseDuration = TimeSpan.FromSeconds(3), PollInterval = TimeSpan.FromMilliseconds(20) };
        var runs = 0;
        var runner = NewRunner(options, new LogCollector(), new DelegateHandler(kind, (_, _) =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }));
        Guid id;
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        {
            id = await JobQueue.EnqueueAsync(connection, null, kind, [], null, token);
            var claimed = await JobQueue.ClaimAsync(connection, "stopped", [kind], options.LeaseDuration, token);
            Assert.Equal(id, claimed?.Id);
        }

        Assert.False(await runner.RunOnceAsync(token));
        Assert.Equal(0, runs);

        await WaitAsync(async () => await ScalarAsync<bool>($"SELECT clock_timestamp() > lease_until FROM control_plane.jobs WHERE id = '{id}'"), token);

        Assert.True(await runner.RunOnceAsync(token));
        Assert.Equal(1, runs);
        Assert.Equal("succeeded", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.Equal(2, await ScalarAsync<int>($"SELECT attempts FROM control_plane.jobs WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_runner_that_lost_its_claim_changes_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.fenced";
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await JobQueue.EnqueueAsync(connection, null, kind, [], null, token);
        var lease = TimeSpan.FromMilliseconds(200);
        var lost = await JobQueue.ClaimAsync(connection, "a", [kind], lease, token);
        await Task.Delay(lease * 2, token);
        var current = await JobQueue.ClaimAsync(connection, "b", [kind], TimeSpan.FromMinutes(5), token);

        Assert.NotNull(lost);
        Assert.NotNull(current);
        Assert.False(await JobQueue.RenewAsync(connection, lost, lease, token));
        Assert.False(await JobQueue.SucceedAsync(connection, lost, token));
        Assert.False(await JobQueue.RetryAsync(connection, lost, TimeSpan.Zero, "late", token));
        Assert.False(await JobQueue.FailAsync(connection, lost, "late", token));
        Assert.Equal("running", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{current.Id}'"));
        Assert.True(await JobQueue.SucceedAsync(connection, current, token));
    }

    [Fact]
    public async Task A_runner_claims_only_the_kinds_it_has_a_handler_for()
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await JobQueue.EnqueueAsync(connection, null, "test.other", [], null, token);
        var runner = NewRunner(new JobRunnerOptions(), new LogCollector(), new DelegateHandler("test.known", (_, _) => Task.CompletedTask));

        Assert.False(await runner.RunOnceAsync(token));
        Assert.Equal(0, await ScalarAsync<int>("SELECT attempts FROM control_plane.jobs WHERE kind = 'test.other'"));
    }

    [Fact]
    public async Task A_failing_job_is_retried_with_a_growing_delay_and_is_failed_after_ten_attempts_with_a_log_and_a_metric()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.failing";
        var options = new JobRunnerOptions { BaseDelay = TimeSpan.FromSeconds(1), PollInterval = TimeSpan.FromMilliseconds(20) };
        var logs = new LogCollector();
        var runner = NewRunner(options, logs, new DelegateHandler(kind, (_, _) => throw new InvalidOperationException("The cell did not answer.")));
        var measurements = new ConcurrentBag<(long Value, string? Kind)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == ControlPlaneMetrics.JobsFailed)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? jobKind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == ControlPlaneMetrics.JobKindTag)
                {
                    jobKind = tag.Value as string;
                }
            }

            measurements.Add((value, jobKind));
        });
        listener.Start();
        Guid id;
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        {
            id = await JobQueue.EnqueueAsync(connection, null, kind, new JsonObject { ["secret"] = "do-not-store" }, null, token);
        }

        var previous = 0.0;
        for (var attempt = 1; attempt < options.MaxAttempts; attempt++)
        {
            Assert.True(await runner.RunOnceAsync(token));
            Assert.Equal("queued", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
            var delay = await ScalarAsync<double>($"SELECT extract(epoch from next_attempt_at - clock_timestamp())::double precision FROM control_plane.jobs WHERE id = '{id}'");
            Assert.True(delay > previous, $"The delay after attempt {attempt} was {delay} s, after {previous} s before.");
            previous = delay;
            Assert.False(await runner.RunOnceAsync(token));
            await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"UPDATE control_plane.jobs SET next_attempt_at = now() WHERE id = '{id}'", token);
        }

        Assert.DoesNotContain(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Empty(measurements);

        Assert.True(await runner.RunOnceAsync(token));

        Assert.Equal("failed", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.Equal(options.MaxAttempts, await ScalarAsync<int>($"SELECT attempts FROM control_plane.jobs WHERE id = '{id}'"));
        var error = await ScalarAsync<string>($"SELECT last_error FROM control_plane.jobs WHERE id = '{id}'");
        Assert.Equal("System.InvalidOperationException: The cell did not answer.", error);
        var logged = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(id.ToString(), logged.Message, StringComparison.Ordinal);
        Assert.Contains(kind, logged.Message, StringComparison.Ordinal);
        var measurement = Assert.Single(measurements);
        Assert.Equal((1L, kind), measurement);
        Assert.False(await runner.RunOnceAsync(token));
    }

    [Fact]
    public async Task A_job_claimed_past_the_last_attempt_after_its_runner_stopped_is_failed_without_running()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.exhausted";
        var options = new JobRunnerOptions { LeaseDuration = TimeSpan.FromMilliseconds(200) };
        var logs = new LogCollector();
        var runs = 0;
        var runner = NewRunner(options, logs, new DelegateHandler(kind, (_, _) =>
        {
            Interlocked.Increment(ref runs);
            return Task.CompletedTask;
        }));
        Guid id;
        await using (var connection = await _dataSource.OpenConnectionAsync(token))
        {
            id = await JobQueue.EnqueueAsync(connection, null, kind, [], null, token);
        }

        await ControlPlaneDatabase.ExecuteAsync(
            _dataSource,
            $"UPDATE control_plane.jobs SET state = 'running', attempts = {options.MaxAttempts}, lease_owner = 'stopped', lease_until = now() WHERE id = '{id}'",
            token);

        Assert.True(await runner.RunOnceAsync(token));

        Assert.Equal(0, runs);
        Assert.Equal("failed", await ScalarAsync<string>($"SELECT state FROM control_plane.jobs WHERE id = '{id}'"));
        Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_job_enqueued_in_a_transaction_that_rolls_back_is_never_queued()
    {
        var token = TestContext.Current.CancellationToken;
        const string kind = "test.rollback";
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using (var transaction = await connection.BeginTransactionAsync(token))
        {
            await JobQueue.EnqueueAsync(connection, transaction, kind, [], null, token);
            await transaction.RollbackAsync(token);
        }

        Assert.Equal(0, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{kind}'"));
    }

    private JobRunner NewRunner(JobRunnerOptions options, LogCollector logs, params IJobHandler[] handlers) =>
        new(_dataSource, handlers, options, new LoggerFactory([logs]).CreateLogger<JobRunner>());

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 60 seconds.");
            await Task.Delay(50, cancellationToken);
        }
    }

    private sealed class DelegateHandler(string kind, Func<ClaimedJob, CancellationToken, Task> run) : IJobHandler
    {
        public string Kind => kind;

        public Task RunAsync(ClaimedJob job, CancellationToken cancellationToken) => run(job, cancellationToken);
    }
}
