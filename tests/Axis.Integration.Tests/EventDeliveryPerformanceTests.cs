using System.Diagnostics;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A thousand raised <c>RequestNoted</c> events are delivered to their two <c>async</c> triggers
/// each within the time limit. StepApp's <c>ApproveNotedRequests</c> starts <c>RoleApprove</c> for
/// each of them, and <c>DecideHugeNotedRequests</c> skips each.
/// </summary>
public sealed class EventDeliveryPerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int Events = 1_000;

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    // Each delivery reads the occurrence, decides and writes a receipt for each trigger, and starts
    // one instance, about 12 ms in all, so the run is expected to take about 15 seconds. The bound
    // is eight times that, so shared CI runners do not make the test flaky, and still low enough to
    // catch a slowdown of an order of magnitude, such as a lost index on the event or receipt table.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thousand_events_are_delivered_to_their_two_triggers_within_the_time_limit()
    {
        // The ratio is 125, so only ApproveNotedRequests starts its process. Raising the events
        // runs the Announce steps only, so no delivery runs yet. It is not timed.
        for (var i = 0; i < Events; i++)
        {
            var subjectId = await fixture.InsertRequestAsync(500m, 4);
            await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);
        }

        while (await fixture.RunNextStepAsync(CancellationToken))
        {
        }

        Assert.Equal((long)Events, await fixture.CountAsync("SELECT count(*) FROM axis.events"));
        Assert.Equal(
            (long)Events,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.EventDeliverWorkItemKind)));

        var deliveries = 0;
        var stopwatch = Stopwatch.StartNew();
        while (await fixture.ClaimAsync(ProcessStarts.EventDeliverWorkItemKind, _lease, CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await fixture.Runner.ExecuteAsync(item, CancellationToken));
            deliveries++;
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Events} events were delivered in {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The events were delivered in {stopwatch.Elapsed}.");
        Assert.Equal(Events, deliveries);

        Assert.True(fixture.Model.TryGetProcess("RoleApprove", out var process));
        Assert.Equal(2L * Events, await fixture.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal((long)Events, await fixture.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(
            (long)Events,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", process.Id)));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.EventDeliverWorkItemKind)));
    }
}
