using Axis.ControlPlane.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.ControlPlane.Tests;

public sealed class JobRunnerTests
{
    [Fact]
    public void The_retry_delay_doubles_from_thirty_seconds_and_stops_at_one_hour()
    {
        var options = new JobRunnerOptions();

        var delays = Enumerable.Range(1, 10).Select(attempt => options.RetryDelay(attempt).TotalSeconds);

        Assert.Equal([30, 60, 120, 240, 480, 960, 1920, 3600, 3600, 3600], delays);
    }

    [Fact]
    public async Task A_runner_with_no_handler_is_idle_and_sends_no_query()
    {
        // The data source points at a port nothing listens on, so any query would throw.
        await using var dataSource = NpgsqlDataSource.Create(ControlPlaneFactory.UnreachableConnectionString);
        using var runner = new JobRunner(dataSource, [], new JobRunnerOptions(), NullLogger<JobRunner>.Instance);

        Assert.False(await runner.RunOnceAsync(TestContext.Current.CancellationToken));
        await runner.StartAsync(TestContext.Current.CancellationToken);
        await runner.StopAsync(TestContext.Current.CancellationToken);
    }
}
