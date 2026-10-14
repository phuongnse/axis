using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A package operation output that changes a field a data change trigger watches starts the
/// trigger's process in the output's transaction. The trigger comes from the active release,
/// which is newer than the release the calling instance is pinned to.
/// </summary>
public sealed class PackageOutputTriggerTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private const string Trigger = """
        {
          "id": "b1a92627-eb01-4926-9dc6-634ccf44fa04",
          "kind": "trigger",
          "name": "ReviewTotals",
          "formatVersion": 1,
          "type": "dataChange",
          "entity": "Request",
          "on": ["update"],
          "fields": ["total"],
          "process": "Review"
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_output_that_changes_a_watched_field_starts_the_process_pinned_to_the_active_release()
    {
        var tenant = fixture.Tenant;
        var (subjectId, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var active = await fixture.ActivateCopyWithAsync("triggers/review-totals.json", Trigger);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal(1L, await tenant.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE subject_id = @subject AND id <> @instance AND release_id = @release AND state = 'running'",
            ("subject", subjectId), ("instance", instanceId), ("release", active)));
        Assert.Equal(
            fixture.ReleaseId,
            (Guid)(await tenant.ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @instance", ("instance", instanceId)))!);
        var started = await tenant.ScalarAsync(
            "SELECT xmin::text FROM axis.audit_records WHERE record_id = @subject AND action = 'process.started' AND details ->> 'trigger' = 'ReviewTotals'",
            ("subject", subjectId));
        var updated = await tenant.ScalarAsync(
            "SELECT xmin::text FROM axis.audit_records WHERE process_instance_id = @instance AND action = 'record.updated' AND actor = 'system'",
            ("instance", instanceId));
        Assert.NotNull(started);
        Assert.Equal(updated, started);
    }
}
