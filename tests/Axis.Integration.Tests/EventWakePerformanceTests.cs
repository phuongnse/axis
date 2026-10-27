using System.Diagnostics;
using System.Text.Json.Nodes;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A hundred raised <c>RequestNoted</c> events, each about a record where one StepApp <c>WaitEvent</c>
/// instance waits, are delivered and wake their instances within the time limit, and the steps the
/// instances go on to run too. Each instance passes its <c>waitEvent</c> step exactly once. The plan of
/// the lookup for waiting instances uses an index and never reads the whole instance table, which holds
/// ten thousand other instances.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class EventWakePerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int Records = 100;

    private const int FinishedInstances = 10_000;

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    // Each delivery reads the occurrence, finds one waiting instance, wakes it in a transaction of its own
    // and writes the match, the history row, the audit record and the next item, and the woken instance
    // then runs its end step. That is about 15 ms for each of the hundred records, so the run is expected
    // to take about 2 seconds. The bound is fifteen times that, so shared CI runners do not make the test
    // flaky, and still low enough to catch a slowdown of an order of magnitude, such as a lost index on
    // the instance table. The plan check, not the time, is what catches a lost index.
    private static readonly TimeSpan _runTime = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hundred_waiting_instances_are_woken_by_their_events_within_the_time_limit_using_an_index_to_find_them()
    {
        Assert.True(fixture.Model.TryGetProcess("Review", out var review));
        Assert.True(fixture.Model.TryGetProcess("WaitEvent", out var waitEvent));
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));

        // The table must hold many instances, or the planner reads it in full even when an index exists.
        // The subjects have no record: an instance has no foreign key to its subject. This is not timed.
        await fixture.ScalarAsync(
            $"""
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, created_at, ended_at)
            SELECT gen_random_uuid(), @application, @process, @entity, gen_random_uuid(), @release, '{ProcessStarts.Completed}',
                   2, 'check', now() - n * interval '1 second', now() - n * interval '1 second'
            FROM generate_series(1, {FinishedInstances}) AS n
            RETURNING 1
            """,
            ("application", fixture.Model.Manifest.Id),
            ("process", review.Id),
            ("entity", request.Id),
            ("release", fixture.FirstReleaseId));

        // One instance waits on each record. Starting the instances runs only their first step. This is not timed.
        List<(Guid SubjectId, Guid InstanceId)> waiting = [];
        for (var i = 0; i < Records; i++)
        {
            var subjectId = await fixture.InsertRequestAsync(50m, 1);
            waiting.Add((subjectId, await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId)));
        }

        await RunStepsAsync();
        Assert.Equal(
            (long)Records,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND state = @waiting AND step = 'await'",
                ("process", waitEvent.Id),
                ("waiting", ProcessStarts.Waiting)));

        // The ratio is 50, so neither RequestNoted trigger starts a process, and the when of the waiting
        // step holds. Raising the events runs the Announce steps only, so no delivery runs yet. It is not timed.
        foreach (var (subjectId, _) in waiting)
        {
            await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);
        }

        await RunStepsAsync();
        Assert.Equal((long)Records, await fixture.CountAsync("SELECT count(*) FROM axis.events"));
        Assert.Equal(
            (long)Records,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.EventDeliverWorkItemKind)));
        await fixture.ScalarAsync("ANALYZE axis.process_instances; SELECT 1");
        await fixture.ScalarAsync("ANALYZE axis.events; SELECT 1");

        var deliveries = 0;
        var stopwatch = Stopwatch.StartNew();
        while (await fixture.ClaimAsync(ProcessStarts.EventDeliverWorkItemKind, _lease, CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await fixture.Runner.ExecuteAsync(item, CancellationToken));
            deliveries++;
        }

        await RunStepsAsync();
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{Records} waiting instances were woken and run to their end in {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The instances were woken and run to their end in {stopwatch.Elapsed}.");
        Assert.Equal(Records, deliveries);

        Assert.Equal((long)Records, await fixture.CountAsync("SELECT count(*) FROM axis.process_event_matches"));
        Assert.Equal((long)Records, await fixture.CountAsync("SELECT count(DISTINCT process_instance_id) FROM axis.process_event_matches"));
        Assert.Equal(
            (long)Records,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND state = @completed AND step = 'done'",
                ("process", waitEvent.Id),
                ("completed", ProcessStarts.Completed)));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND state = @waiting",
                ("process", waitEvent.Id),
                ("waiting", ProcessStarts.Waiting)));
        Assert.Equal(2L * Records, await fixture.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal((long)Records * 2, await fixture.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'skipped'"));
        foreach (var (_, instanceId) in waiting)
        {
            var passes = (await fixture.HistoryAsync(instanceId))
                .Where(row => row.Step == "await" && row.Output is not null && JsonNode.Parse(row.Output)?["eventId"] is not null);
            Assert.Single(passes);
        }

        // A record with no instance, as a delivery with no waiting instance finds. The planner must reach
        // the instances of a record by an index and must not read the table in full.
        var eventId = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.events LIMIT 1"))!;
        var plan = await QueryPlans.ExplainAsync(
            fixture.DataSource,
            EventDeliveryHandler.WaitingSql,
            command =>
            {
                command.Parameters.AddWithValue("application", fixture.Model.Manifest.Id);
                command.Parameters.AddWithValue("entity", request.Id);
                command.Parameters.AddWithValue("subject", Guid.NewGuid());
                command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
                command.Parameters.AddWithValue("event", eventId);
            },
            CancellationToken);
        QueryPlans.AssertUsesIndex(plan, "ix_process_instances_subject_id", "axis.process_instances");
    }

    private async Task RunStepsAsync()
    {
        while (await fixture.RunNextStepAsync(CancellationToken))
        {
        }
    }
}
