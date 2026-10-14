using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// A connector output that changes a field a data change trigger watches starts the trigger's
/// process. The trigger comes from the active release, which is newer than the release the
/// calling instance is pinned to.
/// </summary>
public sealed class ConnectorOutputTriggerTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private const string Trigger = """
        {
          "id": "0bffd02c-c9a4-46dc-a914-ac022949a878",
          "kind": "trigger",
          "name": "ReviewOrderNumbers",
          "formatVersion": 1,
          "type": "dataChange",
          "entity": "Request",
          "on": ["update"],
          "fields": ["comment"],
          "process": "Review"
        }
        """;

    /// <summary>The id of the <c>Review</c> process in the StepApp fixture.</summary>
    private static readonly Guid _reviewProcessId = Guid.Parse("87df0d06-82a2-4db3-a4e6-3a1b442ef9f7");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_output_that_changes_a_watched_field_starts_the_process_pinned_to_the_active_release()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var active = await tenant.ActivateCopyWithAsync("triggers/review-order-numbers.json", Trigger);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Equal(1L, await tenant.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE subject_id = @subject AND id <> @instance AND process_id = @review AND release_id = @release AND state = 'running'
            """,
            ("subject", subjectId), ("instance", instanceId), ("review", _reviewProcessId), ("release", active)));
        Assert.Equal(1L, await tenant.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE record_id = @subject AND action = 'process.started' AND actor = 'system' AND details ->> 'trigger' = 'ReviewOrderNumbers'",
            ("subject", subjectId)));
        Assert.Equal(
            tenant.FirstReleaseId,
            (Guid)(await tenant.ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @instance", ("instance", instanceId)))!);
    }
}
