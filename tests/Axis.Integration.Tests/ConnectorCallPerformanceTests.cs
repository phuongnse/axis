using System.Diagnostics;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A thousand staged connector calls against a fast stub are all sent by one worker within the
/// time limit, and each ends <c>succeeded</c>.
/// </summary>
public sealed class ConnectorCallPerformanceTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private const int Calls = 1_000;

    // Each call is one claim, one HTTP request to the in-process stub and one commit, about 13 ms
    // each, so the run is expected to take about 15 seconds. The bound is eight times that, so
    // shared CI runners do not make the test flaky, and still low enough to catch a slowdown of an
    // order of magnitude, such as a lost index on the work item claim.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thousand_staged_calls_are_sent_one_by_one_within_the_time_limit_and_all_succeed()
    {
        // Staging runs each instance's step, which queues its call. It is not timed.
        var instances = new Guid[Calls];
        for (var i = 0; i < Calls; i++)
        {
            (_, instances[i]) = await fixture.StageAsync("a", CancellationToken);
        }

        var tenant = fixture.Tenant("a");
        Assert.Equal(
            (long)Calls,
            await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.ConnectorCallWorkItemKind)));

        var runs = 0;
        var stopwatch = Stopwatch.StartNew();
        while (await fixture.RunCallAsync("a", CancellationToken) is { } outcome)
        {
            Assert.Equal(WorkItemOutcome.Completed, outcome);
            runs++;
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Calls} connector calls were sent in {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The calls were sent in {stopwatch.Elapsed}.");
        Assert.Equal(Calls, runs);
        Assert.Equal(
            (long)Calls,
            await tenant.CountAsync(
                "SELECT count(*) FROM axis.connector_calls WHERE process_instance_id = ANY(@ids) AND state = @state",
                ("ids", instances),
                ("state", ConnectorCalls.Succeeded)));
    }
}
