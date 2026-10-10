using System.Diagnostics;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ProcessStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static readonly TimeSpan _lockWaitTimeout = TimeSpan.FromSeconds(30);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Instance_at_the_threshold_takes_the_first_branch_and_completes()
    {
        var subjectId = await fixture.InsertRequestAsync(1000m, 1);
        var instanceId = await fixture.StartAsync("Review", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "reviewed", true), await fixture.InstanceAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["check", "reviewed"], history.Select(row => row.Step));
        Assert.Equal([1L, 2L], history.Select(row => row.Revision));
        Assert.All(history, row => Assert.True(row.FinishedAfterStart));
        Assert.All(history, row => Assert.Null(row.Error));

        var check = history[0];
        Assert.Equal("0", check.Decision);
        Assert.Equal("reviewed", check.Next);
        var input = Assert.IsType<JsonObject>(JsonNode.Parse(check.Input));
        Assert.Equal(subjectId, input["subjectId"]!.GetValue<Guid>());
        Assert.Equal(1L, input["subjectVersion"]!.GetValue<long>());
        Assert.Equal(2, input.Count);

        var end = history[1];
        Assert.Null(end.Decision);
        Assert.Equal(ProcessStarts.Completed, JsonNode.Parse(end.Output!)!["state"]!.GetValue<string>());
        Assert.Equal(0L, await CountWorkItemsAsync(instanceId));
    }

    [Fact]
    public async Task Instance_below_the_threshold_takes_otherwise_and_completes()
    {
        var subjectId = await fixture.InsertRequestAsync(999.99m, 1);
        var instanceId = await fixture.StartAsync("Review", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "skipped", true), await fixture.InstanceAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["check", "skipped"], history.Select(row => row.Step));
        Assert.Equal("otherwise", history[0].Decision);
        Assert.Equal("skipped", history[0].Next);
        Assert.Equal(0L, await CountWorkItemsAsync(instanceId));
    }

    [Fact]
    public async Task Each_step_writes_one_system_audit_record()
    {
        var subjectId = await fixture.InsertRequestAsync(1500m, 1);
        var instanceId = await fixture.StartAsync("Review", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal([AuditActions.ProcessStepCompleted, AuditActions.ProcessCompleted], await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Equal(
            """{"next": "reviewed", "step": "check"}""",
            await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action",
                ("id", instanceId),
                ("action", AuditActions.ProcessStepCompleted)));
        Assert.Equal(
            """{"step": "reviewed"}""",
            await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action",
                ("id", instanceId),
                ("action", AuditActions.ProcessCompleted)));
        Assert.Equal(
            2L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.audit_records
                WHERE process_instance_id = @id AND application_id = @application AND entity_id = @entity AND record_id = @record
                """,
                ("id", instanceId),
                ("application", fixture.Model.Manifest.Id),
                ("entity", RequestEntityId),
                ("record", subjectId)));
    }

    [Fact]
    public async Task Step_whose_instance_revision_changed_after_it_loaded_commits_nothing()
    {
        var subjectId = await fixture.InsertRequestAsync(1000m, 1);
        var instanceId = await fixture.StartAsync("Review", subjectId, fixture.FirstReleaseId);

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using (var raise = new NpgsqlCommand("UPDATE axis.process_instances SET revision = revision + 1 WHERE id = @id", connection, transaction))
        {
            raise.Parameters.AddWithValue("id", instanceId);
            Assert.Equal(1, await raise.ExecuteNonQueryAsync(CancellationToken));
        }

        // The step loads revision 1, then its guarded update waits on the row lock this test holds.
        var run = fixture.RunNextAsync(CancellationToken);
        await WaitUntilBlockedOnLockAsync(run);
        await transaction.CommitAsync(CancellationToken);

        Assert.True(await run);
        Assert.Equal((ProcessStarts.Running, 2L, "check", false), await fixture.InstanceAsync(instanceId));
        Assert.Empty(await fixture.HistoryAsync(instanceId));
        Assert.Empty(await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Equal(0L, await CountWorkItemsAsync(instanceId));
    }

    [Fact]
    public async Task Step_whose_expression_fails_at_run_time_fails_the_instance_with_no_partial_writes()
    {
        var subjectId = await fixture.InsertRequestAsync(10m, 0);
        var instanceId = await fixture.StartAsync("Divide", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Failed, 2L, "check", true), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("check", row.Step);
        Assert.Equal(1L, row.Revision);
        Assert.Contains("Division by zero", row.Error, StringComparison.Ordinal);
        Assert.Null(row.Output);
        Assert.Null(row.Decision);
        Assert.True(row.FinishedAfterStart);
        Assert.Equal(subjectId, JsonNode.Parse(row.Input)!["subjectId"]!.GetValue<Guid>());
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Equal(
            """{"step": "check"}""",
            await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action",
                ("id", instanceId),
                ("action", AuditActions.ProcessFailed)));
        Assert.Equal(0L, await CountWorkItemsAsync(instanceId));
    }

    [Fact]
    public async Task Instance_started_before_a_new_release_runs_the_steps_of_its_own_release()
    {
        var pinnedSubject = await fixture.InsertRequestAsync(1000m, 1);
        var pinned = await fixture.StartAsync("Review", pinnedSubject, fixture.FirstReleaseId);

        var renamedReleaseId = await fixture.ActivateRenamedReleaseAsync();
        var laterSubject = await fixture.InsertRequestAsync(1000m, 1);
        var later = await fixture.StartAsync("Review", laterSubject, renamedReleaseId);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "reviewed", true), await fixture.InstanceAsync(pinned));
        Assert.Equal(["check", "reviewed"], (await fixture.HistoryAsync(pinned)).Select(row => row.Step));

        // The new release's own instances take its renamed steps.
        Assert.Equal((ProcessStarts.Completed, 3L, "reviewedV2", true), await fixture.InstanceAsync(later));
        Assert.Equal(["check", "reviewedV2"], (await fixture.HistoryAsync(later)).Select(row => row.Step));
    }

    private Guid RequestEntityId
    {
        get
        {
            Assert.True(fixture.Model.TryGetEntity("Request", out var request));
            return request.Id;
        }
    }

    private Task<long> CountWorkItemsAsync(Guid instanceId) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instanceId));

    /// <summary>Waits until one session of the tenant database waits on a lock, and fails when <paramref name="run"/> ends first.</summary>
    private async Task WaitUntilBlockedOnLockAsync(Task run)
    {
        var clock = Stopwatch.StartNew();
        while (await fixture.CountAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") != 1)
        {
            Assert.False(run.IsCompleted, "The step ended before it waited on the instance's row lock.");
            Assert.True(clock.Elapsed < _lockWaitTimeout, "The step never waited on the instance's row lock.");
            await Task.Delay(TimeSpan.FromMilliseconds(20), CancellationToken);
        }
    }
}
