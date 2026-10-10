using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class ProcessTaskStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Task_step_for_the_department_manager_creates_an_open_task_and_the_instance_waits()
    {
        var department = await fixture.InsertDepartmentAsync("maria");
        var subjectId = await fixture.InsertRequestAsync(500m, 1, department);
        var instanceId = await fixture.StartAsync("Approve", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Waiting, 2L, "approve", false), await fixture.InstanceAsync(instanceId));
        var task = Assert.Single(await fixture.TasksAsync(instanceId));
        Assert.Equal((ProcessTasks.Open, ProcessTasks.UserAssignee, "maria"), (task.State, task.AssigneeKind, task.Assignee));
        Assert.True(fixture.Model.TryGetForm("RequestReview", out var form));
        Assert.Equal(form.Id, task.FormId);
        Assert.Equal(task.CreatedAt + TimeSpan.FromDays(3), task.DueAt);
        Assert.Equal(TimeSpan.FromDays(3), task.DueIn);

        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("approve", 1L, null), (row.Step, row.Revision, row.Error));
        var output = JsonNode.Parse(row.Output!)!;
        Assert.Equal(ProcessStarts.Waiting, output["state"]!.GetValue<string>());
        Assert.Equal(task.Id, output["taskId"]!.GetValue<Guid>());

        Assert.Equal([AuditActions.TaskCreated], await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Equal(
            $$"""{"step": "approve", "taskId": "{{task.Id}}"}""",
            (string?)await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
                ("id", instanceId),
                ("action", AuditActions.TaskCreated)));
        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
    }

    [Fact]
    public async Task Task_step_for_a_role_stores_the_role_name_and_no_due_date_without_due_in()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("RoleApprove", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Waiting, 2L, "approve", false), await fixture.InstanceAsync(instanceId));
        var task = Assert.Single(await fixture.TasksAsync(instanceId));
        Assert.Equal((ProcessTasks.Open, ProcessTasks.RoleAssignee, "finance"), (task.State, task.AssigneeKind, task.Assignee));
        Assert.Null(task.DueAt);
        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
    }

    [Fact]
    public async Task Task_step_whose_user_expression_gives_null_fails_the_instance_and_stores_no_task()
    {
        var department = await fixture.InsertDepartmentAsync(manager: null);
        var subjectId = await fixture.InsertRequestAsync(500m, 1, department);
        var instanceId = await fixture.StartAsync("Approve", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Failed, 2L, "approve", true), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("The assignee of the task step 'approve' gave no user.", row.Error);
        Assert.Null(row.Output);
        Assert.Empty(await fixture.TasksAsync(instanceId));
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
    }

    private Task<long> WorkItemCountAsync(Guid instanceId) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instanceId));
}
