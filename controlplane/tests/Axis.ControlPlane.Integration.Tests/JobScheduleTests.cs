using Axis.ControlPlane.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The job schedule against a real PostgreSQL, calling its queueing step directly.</summary>
public sealed class JobScheduleTests : IAsyncLifetime
{
    private const string Kind = "correctDirectory";

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
    public async Task One_job_is_queued_for_each_interval_while_the_last_is_queued_running_or_young()
    {
        var token = TestContext.Current.CancellationToken;
        var schedule = Schedule(new RecurringJob(Kind, TimeSpan.FromMinutes(5)));

        Assert.Equal(1, await schedule.EnqueueDueAsync(token));
        Assert.Equal(0, await schedule.EnqueueDueAsync(token));

        // The job ended, but it was created less than 5 minutes ago.
        await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"UPDATE control_plane.jobs SET state = 'succeeded' WHERE kind = '{Kind}'", token);
        Assert.Equal(0, await schedule.EnqueueDueAsync(token));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{Kind}'"));

        // Once it is older than the interval, the next one is queued.
        await ControlPlaneDatabase.ExecuteAsync(_dataSource, $"UPDATE control_plane.jobs SET created_at = created_at - interval '6 minutes' WHERE kind = '{Kind}'", token);
        Assert.Equal(1, await schedule.EnqueueDueAsync(token));
        Assert.Equal(2L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{Kind}'"));
    }

    [Fact]
    public async Task A_job_that_is_still_running_after_the_interval_keeps_the_next_one_from_being_queued()
    {
        var token = TestContext.Current.CancellationToken;
        var schedule = Schedule(new RecurringJob(Kind, TimeSpan.FromMinutes(5)));
        Assert.Equal(1, await schedule.EnqueueDueAsync(token));

        await ControlPlaneDatabase.ExecuteAsync(
            _dataSource, $"UPDATE control_plane.jobs SET state = 'running', created_at = created_at - interval '1 hour' WHERE kind = '{Kind}'", token);

        Assert.Equal(0, await schedule.EnqueueDueAsync(token));
    }

    [Fact]
    public async Task Parallel_schedules_queue_one_job_between_them()
    {
        var token = TestContext.Current.CancellationToken;
        var entry = new RecurringJob(Kind, TimeSpan.FromMinutes(5));
        var schedules = Enumerable.Range(0, 8).Select(_ => Schedule(entry)).ToList();

        var queued = await Task.WhenAll(schedules.Select(schedule => schedule.EnqueueDueAsync(token)));

        Assert.Equal(1, queued.Sum());
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{Kind}'"));
    }

    [Fact]
    public async Task A_schedule_without_entries_queues_nothing()
    {
        var schedule = Schedule();

        Assert.Equal(0, await schedule.EnqueueDueAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
    }

    private JobSchedule Schedule(params RecurringJob[] entries) =>
        new(_dataSource, entries, TimeProvider.System, NullLogger<JobSchedule>.Instance);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);
}
