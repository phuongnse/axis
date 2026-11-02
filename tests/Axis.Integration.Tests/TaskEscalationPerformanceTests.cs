using System.Diagnostics;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// Five hundred <c>task.escalate</c> and five hundred <c>task.deadline</c> items are due on the same five hundred human
/// tasks. The worker runs all of them within the time limit. Each escalation reassigns its task to the <c>reviewer</c>
/// role and stores an in-app notification for that role, and each deadline then expires its task.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class TaskEscalationPerformanceTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const int Tasks = 500;

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    // An escalation locks its task, reassigns it, writes the audit record and the notification, and replaces the
    // history of the step. A deadline does about as much. Each item takes about 4 ms, so the run is expected to take
    // about 5 seconds. The bound is twenty times that, so shared CI runners do not make the test flaky, and still low
    // enough to catch a slowdown of an order of magnitude, such as a lost index on the task or work item table.
    private static readonly TimeSpan _runTime = TimeSpan.FromMinutes(2);

    // The task of the process starts with the finance role. Its one escalation reassigns it to the reviewer role and
    // notifies that role. A deadline of one day moves the instance to its expired end step.
    private const string Process = """
        {
          "id": "b6a1c7a0-5e0b-4d36-9a57-2d9f0c6f3e51",
          "kind": "process",
          "name": "EscalateAndNotify",
          "formatVersion": 1,
          "entity": "Request",
          "start": "decide",
          "steps": [
            {
              "name": "decide",
              "type": "task",
              "label": { "textKey": "request.decide" },
              "assignee": { "role": "finance" },
              "form": "RequestDecision",
              "outcomes": [
                { "name": "approve", "label": { "textKey": "task.approve" }, "next": "approved" },
                { "name": "reject", "label": { "textKey": "task.reject" }, "next": "rejected" }
              ],
              "deadline": { "after": "P1D", "next": "expired" },
              "escalations": [
                {
                  "name": "escalation1",
                  "after": "PT1H",
                  "reassign": { "role": "reviewer" },
                  "notify": {
                    "to": [{ "role": "reviewer" }],
                    "channels": ["inApp"],
                    "message": {
                      "subject": { "textKey": "request.notice.subject" },
                      "body": { "textKey": "request.notice.body" },
                      "values": { "title": "title", "amount": "amount" }
                    },
                    "open": { "page": "Tasks" }
                  }
                }
              ]
            },
            { "name": "approved", "type": "end" },
            { "name": "rejected", "type": "end" },
            { "name": "expired", "type": "end" }
          ]
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_thousand_due_escalation_and_deadline_items_run_within_the_time_limit()
    {
        // Every instance waits on its task and has one escalation item and one deadline item. Starting them is not timed.
        var (releaseId, model) = await fixture.ActivateCopyWithFilesAsync(("processes/escalate-and-notify.json", Process));
        Assert.True(model.TryGetProcess("EscalateAndNotify", out var process));
        for (var i = 0; i < Tasks; i++)
        {
            var subjectId = await fixture.InsertRequestAsync(500m, 4);
            await fixture.StartAsync("EscalateAndNotify", subjectId, releaseId, model);
        }

        while (await fixture.RunNextStepAsync(CancellationToken))
        {
        }

        var processId = ("process", (object)process.Id);
        var instances = "(SELECT id FROM axis.process_instances WHERE process_id = @process)";
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync($"SELECT count(*) FROM axis.process_tasks WHERE state = 'open' AND process_instance_id IN {instances}", processId));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TaskEscalateWorkItemKind)));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TaskDeadlineWorkItemKind)));
        await fixture.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE kind IN (@escalate, @deadline)",
            ("escalate", ProcessStarts.TaskEscalateWorkItemKind),
            ("deadline", ProcessStarts.TaskDeadlineWorkItemKind));

        // The escalations run first. An expiry deletes the pending escalation items of its task, and the queue claims
        // by due time and then by id, so one loop over both kinds could run a deadline before its escalation.
        var escalations = 0;
        var deadlines = 0;
        var stopwatch = Stopwatch.StartNew();
        while (await fixture.ClaimAsync(ProcessStarts.TaskEscalateWorkItemKind, _lease, CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await fixture.Runner.ExecuteAsync(item, CancellationToken));
            escalations++;
        }

        while (await fixture.ClaimAsync(ProcessStarts.TaskDeadlineWorkItemKind, _lease, CancellationToken) is { } item)
        {
            Assert.Equal(WorkItemOutcome.Completed, await fixture.Runner.ExecuteAsync(item, CancellationToken));
            deadlines++;
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{escalations} escalations and {deadlines} deadlines ran in {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _runTime, $"The items ran in {stopwatch.Elapsed}.");
        Assert.Equal(Tasks, escalations);
        Assert.Equal(Tasks, deadlines);

        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync(
                $"SELECT count(*) FROM axis.process_tasks WHERE state = 'expired' AND assignee_kind = 'role' AND assignee = 'reviewer' AND process_instance_id IN {instances}",
                processId));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync(
                $"SELECT count(*) FROM axis.audit_records WHERE action = @action AND actor = @actor AND process_instance_id IN {instances}",
                processId,
                ("action", AuditActions.TaskReassigned),
                ("actor", AuditActors.System)));
        Assert.Equal((long)Tasks, await fixture.CountAsync("SELECT count(*) FROM axis.notifications"));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync(
                $"SELECT count(*) FROM axis.audit_records WHERE action = @action AND process_instance_id IN {instances}",
                processId,
                ("action", AuditActions.TaskExpired)));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync(
                $"SELECT count(*) FROM axis.process_step_history WHERE decision = 'deadline' AND process_instance_id IN {instances}",
                processId));
        Assert.Equal(
            (long)Tasks,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND step = 'expired'", processId));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE kind IN (@escalate, @deadline)",
                ("escalate", ProcessStarts.TaskEscalateWorkItemKind),
                ("deadline", ProcessStarts.TaskDeadlineWorkItemKind)));
    }
}
