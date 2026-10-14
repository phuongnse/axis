using System.Diagnostics;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// One occurrence of the schedule trigger <c>ReviewLargeRequests</c> fans out over ten thousand
/// matching requests within the time limit, and each request gets exactly one started instance.
/// </summary>
public sealed class ScheduleFanOutPerformanceTests(ScheduleTriggerFixture fixture) : IClassFixture<ScheduleTriggerFixture>
{
    private const int Records = 10_000;

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    // The occurrence runs once and each batch of 100 records is one more run, each about a quarter
    // of a second, so the fan-out is expected to take 30 seconds or less. The bound is four times
    // that, so shared CI runners do not make the test flaky, and still low enough to catch a
    // batch that slows down as the cursor moves, such as one that reads the data source from the
    // start each time.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(2);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task One_occurrence_fans_out_over_ten_thousand_records_within_the_time_limit_and_starts_each_once()
    {
        // The records and the due occurrence are set up before the worker runs. This is not timed.
        var tenant = fixture.Tenant("a");
        await tenant.InsertRequestsAsync(Records, 2000m, 1);
        await tenant.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = @at", ("at", _occurrence));
        await tenant.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = @at WHERE kind = @kind",
            ("at", _occurrence),
            ("kind", ProcessStarts.TriggerScheduleWorkItemKind));
        var worker = fixture.NewWorker();

        var runs = 0;
        var stopwatch = Stopwatch.StartNew();
        await using (var connection = await tenant.DataSource.OpenConnectionAsync(CancellationToken))
        {
            // The schedule item moves to the next occurrence, which is in the future, so the loop ends.
            while (await WorkItemQueue.ClaimAsync(
                connection,
                tenant.Tenant,
                [ProcessStarts.TriggerScheduleWorkItemKind, ProcessStarts.TriggerFanOutWorkItemKind],
                _lease,
                CancellationToken) is { } item)
            {
                await worker.ExecuteAsync(item, CancellationToken);
                runs++;
            }
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The fan-out over {Records} records took {stopwatch.Elapsed} in {runs} runs.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The fan-out took {stopwatch.Elapsed}.");
        // The occurrence, the 100 full batches and the last empty batch that shows the end.
        Assert.Equal(1 + Records / 100 + 1, runs);

        Assert.Equal((long)Records, await tenant.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal((long)Records, await tenant.CountAsync("SELECT count(DISTINCT subject_id) FROM axis.process_instances"));
        Assert.Equal((long)Records, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(
            0L,
            await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));
    }
}
