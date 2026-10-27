using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ProcessWaitEventTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string Noted = """{"title":"t","ratio":2}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Event_raised_before_the_step_ran_moves_the_instance_on_at_once_with_one_match_row()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        var eventId = await InsertEventAsync(subjectId, instanceId, Noted);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["await", "done"], history.Select(row => row.Step));
        var output = JsonNode.Parse(history[0].Output!)!;
        Assert.Equal(("done", eventId), (output["next"]!.GetValue<string>(), output["eventId"]!.GetValue<Guid>()));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_event_matches WHERE process_instance_id = @id AND event_id = @event AND step = 'await'",
                ("id", instanceId),
                ("event", eventId)));
        Assert.Equal(1L, await MatchCountAsync(instanceId));
        Assert.Equal(0L, await TimerCountAsync(instanceId));
        Assert.Equal("""{"next": "done", "step": "await"}""", await StepCompletedDetailsAsync(instanceId));
    }

    [Fact]
    public async Task Earliest_event_is_used_and_each_event_is_used_once()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        var first = await InsertEventAsync(subjectId, instanceId, Noted, "now() + interval '1 minute'");
        await InsertEventAsync(subjectId, instanceId, Noted, "now() + interval '2 minutes'");

        await fixture.RunUntilIdleAsync(CancellationToken);

        var output = JsonNode.Parse((await fixture.HistoryAsync(instanceId))[0].Output!)!;
        Assert.Equal(first, output["eventId"]!.GetValue<Guid>());
        Assert.Equal(1L, await MatchCountAsync(instanceId));
    }

    [Fact]
    public async Task Event_about_another_record_or_with_a_false_when_or_raised_before_the_instance_leaves_it_waiting()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var otherId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        await InsertEventAsync(otherId, instanceId, Noted);
        await InsertEventAsync(subjectId, instanceId, """{"title":"t","ratio":0}""");
        await InsertEventAsync(subjectId, instanceId, Noted, "now() - interval '1 day'");

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Waiting, 2L, "await", false), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("""{"state": "waiting"}""", row.Output);
        Assert.Equal(0L, await MatchCountAsync(instanceId));
        Assert.Equal(1L, await TimerCountAsync(instanceId));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.process_work_items w, axis.process_step_history h
                WHERE w.process_instance_id = @id AND w.kind = @kind AND w.revision = 2
                  AND h.process_instance_id = @id AND h.step = 'await' AND w.due_at = h.started_at + interval '1 hour'
                """,
                ("id", instanceId),
                ("kind", ProcessStarts.TimerWorkItemKind)));
        Assert.Empty(await fixture.SystemAuditActionsAsync(instanceId));
    }

    [Fact]
    public async Task Event_with_a_payload_that_makes_the_when_fail_is_no_match_and_does_not_fail_the_step()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        await InsertEventAsync(subjectId, instanceId, """{"title":"t","ratio":"not a number"}""");

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Waiting, 2L, "await", false), await fixture.InstanceAsync(instanceId));
        Assert.Equal(0L, await MatchCountAsync(instanceId));
    }

    [Fact]
    public async Task Due_timeout_moves_the_waiting_instance_to_its_timeout_step()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.InstanceAsync(instanceId)).State);

        await fixture.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE process_instance_id = @id AND kind = @kind RETURNING 1",
            ("id", instanceId),
            ("kind", ProcessStarts.TimerWorkItemKind));
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 4L, "timedOut", true), await fixture.InstanceAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["await", "await", "timedOut"], history.Select(row => row.Step));
        Assert.Equal("""{"next": "timedOut", "timeout": true}""", history[1].Output);
        Assert.Equal(0L, await MatchCountAsync(instanceId));
        Assert.Equal("""{"next": "timedOut", "step": "await"}""", await StepCompletedDetailsAsync(instanceId));
    }

    [Fact]
    public async Task Step_waits_on_the_record_lock_while_another_transaction_holds_it()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("WaitEvent", subjectId, fixture.FirstReleaseId);
        await InsertEventAsync(subjectId, instanceId, Noted);

        await using var connection = await fixture.DataSource.OpenConnectionAsync(CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction))
        {
            hold.Parameters.AddWithValue("key", RecordEventLock.Key(subjectId));
            await hold.ExecuteNonQueryAsync(CancellationToken);
        }

        var run = fixture.RunUntilIdleAsync(CancellationToken);
        await fixture.WaitUntilBlockedOnLockAsync(run, CancellationToken);
        Assert.Equal(ProcessStarts.Running, (await fixture.InstanceAsync(instanceId)).State);
        await transaction.RollbackAsync(CancellationToken);
        await run;

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        Assert.Equal(1L, await MatchCountAsync(instanceId));
    }

    /// <summary>
    /// Writes an occurrence of <c>RequestNoted</c> about <paramref name="subjectId"/> straight into the
    /// table, so no trigger of the application starts another process. The instance only stands for the raiser.
    /// </summary>
    private async Task<Guid> InsertEventAsync(Guid subjectId, Guid instanceId, string payload, string raisedAt = "now()")
    {
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        var id = Guid.CreateVersion7();
        await fixture.ScalarAsync(
            $"""
            INSERT INTO axis.events (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            VALUES (@id, @application, 'RequestNoted', @release, @entity, @subject, @payload::jsonb, @instance, {raisedAt}) RETURNING 1
            """,
            ("id", id),
            ("application", fixture.Model.Manifest.Id),
            ("release", fixture.FirstReleaseId),
            ("entity", request.Id),
            ("subject", subjectId),
            ("payload", payload),
            ("instance", instanceId));
        return id;
    }

    private Task<long> MatchCountAsync(Guid instanceId) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_event_matches WHERE process_instance_id = @id", ("id", instanceId));

    private Task<long> TimerCountAsync(Guid instanceId) =>
        fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = @kind",
            ("id", instanceId),
            ("kind", ProcessStarts.TimerWorkItemKind));

    private async Task<string?> StepCompletedDetailsAsync(Guid instanceId) =>
        (string?)await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system' AND details->>'step' = 'await'",
            ("id", instanceId),
            ("action", AuditActions.ProcessStepCompleted));
}
