using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class ProcessRaiseEventStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static readonly TimeSpan _shortLease = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan _afterShortLease = TimeSpan.FromSeconds(1.5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Raise_event_step_stores_one_occurrence_with_its_evaluated_payload_and_moves_on()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));
        Assert.True(fixture.Model.TryGetEntity("Request", out var request));
        var occurrence = Assert.Single(await EventsAsync(instanceId));
        Assert.Equal(
            (fixture.Model.Manifest.Id, "RequestNoted", fixture.FirstReleaseId, request.Id, subjectId),
            (occurrence.ApplicationId, occurrence.Event, occurrence.ReleaseId, occurrence.EntityId, occurrence.SubjectId));
        Assert.Equal(7, occurrence.Id.Version);

        // The payload holds every payload field, keyed by its declared name.
        var payload = Assert.IsType<JsonObject>(JsonNode.Parse(occurrence.Payload));
        Assert.Equal(["ratio", "title"], payload.Select(entry => entry.Key).Order(StringComparer.Ordinal));
        Assert.Equal($"Request {subjectId:N}", payload["title"]!.GetValue<string>());
        Assert.Equal(125m, payload["ratio"]!.GetValue<decimal>());
    }

    [Fact]
    public async Task Raise_event_step_queues_one_delivery_of_its_occurrence_in_its_own_transaction()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        // Only the raising step runs, so its delivery is still queued.
        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        var occurrence = Assert.Single(await EventsAsync(instanceId));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE kind = @kind AND event_id = @id AND process_instance_id IS NULL",
                ("kind", ProcessStarts.EventDeliverWorkItemKind),
                ("id", occurrence.Id)));

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", occurrence.Id)));
    }

    [Fact]
    public async Task Raise_event_step_writes_its_history_and_step_audit_record_in_the_same_transaction_as_the_occurrence()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        var occurrence = Assert.Single(await EventsAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["announce", "done"], history.Select(row => row.Step));
        var row = history[0];
        Assert.Equal((1L, (string?)null, (string?)null), (row.Revision, row.Decision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["next"] = "done", ["eventId"] = occurrence.Id },
            JsonNode.Parse(row.Output!)));

        // The occurrence was raised at the start of the step's transaction, as its history row started.
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.events e
                JOIN axis.process_step_history h ON h.process_instance_id = e.process_instance_id AND h.step = 'announce' AND h.started_at = e.raised_at
                WHERE e.process_instance_id = @id
                """,
                ("id", instanceId)));

        Assert.Equal([AuditActions.ProcessStepCompleted, AuditActions.ProcessCompleted], await fixture.SystemAuditActionsAsync(instanceId));
        var details = (string?)await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instanceId),
            ("action", AuditActions.ProcessStepCompleted));
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["step"] = "announce", ["next"] = "done" }, JsonNode.Parse(details!)));
    }

    [Fact]
    public async Task Payload_field_that_gives_null_is_stored_as_null()
    {
        var subjectId = await fixture.InsertRequestAsync(null, 4);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        var payload = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.Single(await EventsAsync(instanceId)).Payload));
        Assert.True(payload.ContainsKey("ratio"));
        Assert.Null(payload["ratio"]);
    }

    [Fact]
    public async Task Payload_expression_that_divides_by_zero_fails_the_instance_and_stores_no_occurrence()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 0);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Failed, 2L, "announce", true), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("announce", (string?)null), (row.Step, row.Output));
        Assert.NotNull(row.Error);
        Assert.Empty(await EventsAsync(instanceId));
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
    }

    [Fact]
    public async Task Raise_event_step_of_a_worker_that_lost_its_lease_stores_exactly_one_occurrence()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 4);
        var instanceId = await fixture.StartAsync("Announce", subjectId, fixture.FirstReleaseId);

        // The first worker claims the step's item and stalls until its lease expires.
        var stalled = await fixture.Runner.ClaimAsync(ProcessStepFixture.TenantId, _shortLease, CancellationToken);
        Assert.Equal(instanceId, stalled?.ProcessInstanceId);
        Assert.Empty(await EventsAsync(instanceId));

        await Task.Delay(_afterShortLease, CancellationToken);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var occurrence = Assert.Single(await EventsAsync(instanceId));
        Assert.Equal((ProcessStarts.Completed, 3L, "done", true), await fixture.InstanceAsync(instanceId));

        // The stalled worker's claim is gone, so its late run commits nothing.
        Assert.Equal(WorkItemOutcome.LostClaim, await fixture.Runner.ExecuteAsync(stalled!, CancellationToken));
        Assert.Equal(occurrence.Id, Assert.Single(await EventsAsync(instanceId)).Id);
        Assert.Equal(["announce", "done"], (await fixture.HistoryAsync(instanceId)).Select(row => row.Step));
    }

    /// <summary>The event occurrences the instance raised, read from SQL.</summary>
    private async Task<IReadOnlyList<EventRow>> EventsAsync(Guid instanceId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT id, application_id, event, release_id, entity_id, subject_id, payload::text
            FROM axis.events WHERE process_instance_id = @id ORDER BY id
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<EventRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new EventRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.GetGuid(5),
                reader.GetString(6)));
        }

        return rows;
    }

    private sealed record EventRow(
        Guid Id,
        Guid ApplicationId,
        string Event,
        Guid ReleaseId,
        Guid EntityId,
        Guid SubjectId,
        string Payload);
}
