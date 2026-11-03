using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;

namespace Axis.Integration.Tests;

/// <summary>
/// A <c>subProcess</c> step starts its child on the release the parent is pinned to and puts the parent in
/// <c>waiting</c>. When the child completes, a <c>process.resume</c> item moves the parent on to the step's
/// <c>next</c>. A child that cannot start fails the parent's step with a fixed text. Each test activates a
/// copy of StepApp with its own processes, so the tests of the class do not meet on the unique index of a
/// process and record.
/// </summary>
public sealed class ProcessSubProcessStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private const string CloseAndFinish = """{ "name": "close", "type": "end" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_child_runs_on_the_parents_release_after_a_newer_release_and_resumes_the_parent_at_next()
    {
        var (first, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/release-parent.json", Parent("a1f0c3d2-5b7e-4c19-8d46-0e2b7a9c1f01", "ReleaseParent", "ReleaseChild", """ "next": "close" """)),
            ("processes/release-child.json", Child("a1f0c3d2-5b7e-4c19-8d46-0e2b7a9c1f02", "ReleaseChild", "", "finish")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("ReleaseParent", request, first, model);

        // The child's end step is renamed in the newer release, so the child shows which release ran it.
        var (second, _) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/release-parent.json", Parent("a1f0c3d2-5b7e-4c19-8d46-0e2b7a9c1f01", "ReleaseParent", "ReleaseChild", """ "next": "close" """)),
            ("processes/release-child.json", Child("a1f0c3d2-5b7e-4c19-8d46-0e2b7a9c1f02", "ReleaseChild", "", "finishV2")));
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.NotEqual(first, second);
        var childId = await ChildIdAsync("ReleaseChild", model);
        Assert.Equal(first, await fixture.ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @id", ("id", childId)));
        Assert.Equal(parentId, await fixture.ScalarAsync("SELECT parent_instance_id FROM axis.process_instances WHERE id = @id", ("id", childId)));
        Assert.Equal("fulfil", await fixture.ScalarAsync("SELECT parent_step FROM axis.process_instances WHERE id = @id", ("id", childId)));
        var child = await fixture.InstanceAsync(childId);
        Assert.Equal(("completed", "finish", true), (child.State, child.Step, child.Ended));
        Assert.Equal(["finish"], (await fixture.HistoryAsync(childId)).Select(row => row.Step));

        var parent = await fixture.InstanceAsync(parentId);
        Assert.Equal(("completed", "close", true), (parent.State, parent.Step, parent.Ended));
        var history = await fixture.HistoryAsync(parentId);
        Assert.Equal(["fulfil", "fulfil", "close"], history.Select(row => row.Step));
        Assert.All(history, row => Assert.Null(row.Error));
        var waiting = JsonNode.Parse(history[0].Output!)!.AsObject();
        Assert.Equal("waiting", (string?)waiting["state"]);
        Assert.Equal(childId, (Guid?)waiting["childInstanceId"]);
        var resumed = JsonNode.Parse(history[1].Output!)!.AsObject();
        Assert.Equal("close", (string?)resumed["next"]);
        Assert.Equal(childId, (Guid?)resumed["childInstanceId"]);
        Assert.Equal(
            [AuditActions.ProcessStepCompleted, AuditActions.ProcessCompleted],
            (await fixture.SystemAuditActionsAsync(parentId)).Where(action => action != AuditActions.ProcessStarted));
        Assert.Equal(0, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id IN (@parent, @child)", ("parent", parentId), ("child", childId)));
    }

    [Fact]
    public async Task A_child_takes_over_the_accountable_user_and_the_deciding_task_of_its_parent()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/accountable-parent.json", Parent("f6c5b8a7-a0c3-4161-b29b-5d70ce416e01", "AccountableParent", "AccountableChild", """ "next": "close" """)),
            ("processes/accountable-child.json", Child("f6c5b8a7-a0c3-4161-b29b-5d70ce416e02", "AccountableChild", "", "finish")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("AccountableParent", request, release, model);
        var decisionTask = Guid.CreateVersion7();
        await fixture.ScalarAsync(
            "UPDATE axis.process_instances SET accountable_user = 'maria', decision_task = @task WHERE id = @parent",
            ("task", decisionTask),
            ("parent", parentId));

        await fixture.RunUntilIdleAsync(CancellationToken);

        var childId = await ChildIdAsync("AccountableChild", model);
        Assert.Equal("maria", await fixture.ScalarAsync("SELECT accountable_user FROM axis.process_instances WHERE id = @id", ("id", childId)));
        Assert.Equal(decisionTask, await fixture.ScalarAsync("SELECT decision_task FROM axis.process_instances WHERE id = @id", ("id", childId)));
    }

    [Fact]
    public async Task A_child_that_already_runs_for_the_record_fails_the_parents_step_and_starts_no_second_child()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/running-parent.json", Parent("b2e1d4c3-6c8f-4d2a-9e57-1f3c8b0d2a01", "RunningParent", "RunningChild", """ "next": "close" """)),
            ("processes/running-child.json", Child("b2e1d4c3-6c8f-4d2a-9e57-1f3c8b0d2a02", "RunningChild", "", "finish")));
        var request = await fixture.InsertRequestAsync(10, 2);

        // The child runs for the record already, and its work item is gone, so it stays running.
        var running = await fixture.StartAsync("RunningChild", request, release, model);
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", running));
        var parentId = await fixture.StartAsync("RunningParent", request, release, model);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var parent = await fixture.InstanceAsync(parentId);
        Assert.Equal(("failed", "fulfil", true), (parent.State, parent.Step, parent.Ended));
        var history = await fixture.HistoryAsync(parentId);
        var failure = Assert.Single(history);
        Assert.Equal("fulfil", failure.Step);
        Assert.Equal("The step 'fulfil' cannot start the process 'RunningChild': it already runs for the record.", failure.Error);
        Assert.Equal(1, await ChildCountAsync("RunningChild", model));
        Assert.Equal("running", (await fixture.InstanceAsync(running)).State);
        Assert.Equal(0, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE parent_instance_id = @id", ("id", parentId)));
    }

    [Fact]
    public async Task A_child_without_a_subject_is_stored_with_the_mapped_parameters_and_its_start_names_the_parent()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/param-parent.json", Parent("c3f2e5d4-7d90-4e3b-8f68-2a4d9c1e3b01", "ParamParent", "ParamChild", """ "parameters": { "days": "divisor" }, "next": "close" """)),
            ("processes/param-child.json", """
                {
                  "id": "c3f2e5d4-7d90-4e3b-8f68-2a4d9c1e3b02",
                  "kind": "process",
                  "name": "ParamChild",
                  "formatVersion": 1,
                  "parameters": [{ "name": "days", "type": "integer" }],
                  "start": "finish",
                  "steps": [{ "name": "finish", "type": "end" }]
                }
                """));
        var request = await fixture.InsertRequestAsync(10, 5);
        var parentId = await fixture.StartAsync("ParamParent", request, release, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        var childId = await ChildIdAsync("ParamChild", model);
        Assert.Equal(1, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE id = @id AND subject_id IS NULL AND subject_entity_id IS NULL AND parameters->>'days' = '5' AND parent_instance_id = @parent",
            ("id", childId),
            ("parent", parentId)));
        Assert.Equal(parentId.ToString("D"), await fixture.ScalarAsync(
            "SELECT details->>'parentInstanceId' FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", childId),
            ("action", AuditActions.ProcessStarted)));
        Assert.Equal("completed", (await fixture.InstanceAsync(childId)).State);
        Assert.Equal("completed", (await fixture.InstanceAsync(parentId)).State);
    }

    [Fact]
    public async Task A_child_whose_start_condition_is_false_fails_the_parents_step_and_is_not_stored()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/condition-parent.json", Parent("d4a3f6e5-8ea1-4f4c-9079-3b5eac2f4c01", "ConditionParent", "ConditionChild", """ "next": "close" """)),
            ("processes/condition-child.json", Child("d4a3f6e5-8ea1-4f4c-9079-3b5eac2f4c02", "ConditionChild", """ "startCondition": { "expression": "amount > 1000", "message": { "textKey": "request.amountNegative" } }, """, "finish")));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("ConditionParent", request, release, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        var parent = await fixture.InstanceAsync(parentId);
        Assert.Equal(("failed", "fulfil", true), (parent.State, parent.Step, parent.Ended));
        var failure = Assert.Single(await fixture.HistoryAsync(parentId));
        Assert.Equal("The step 'fulfil' cannot start the process 'ConditionChild': its start condition is not true.", failure.Error);
        Assert.Equal(0, await ChildCountAsync("ConditionChild", model));
    }

    [Fact]
    public async Task A_child_runs_for_the_record_the_subject_expression_gives()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/subject-parent.json", Parent("e5b4a7f6-9fb2-405d-a18a-4c6fbd305d01", "SubjectParent", "SubjectChild", """ "subject": "department", "next": "close" """)),
            ("processes/subject-child.json", """
                {
                  "id": "e5b4a7f6-9fb2-405d-a18a-4c6fbd305d02",
                  "kind": "process",
                  "name": "SubjectChild",
                  "formatVersion": 1,
                  "entity": "Department",
                  "start": "finish",
                  "steps": [{ "name": "finish", "type": "end" }]
                }
                """));
        var department = await fixture.InsertDepartmentAsync("mai");
        var request = await fixture.InsertRequestAsync(10, 2, department);
        var parentId = await fixture.StartAsync("SubjectParent", request, release, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        var childId = await ChildIdAsync("SubjectChild", model);
        Assert.Equal(department, await fixture.ScalarAsync("SELECT subject_id FROM axis.process_instances WHERE id = @id", ("id", childId)));
        Assert.Equal("completed", (await fixture.InstanceAsync(childId)).State);
        Assert.Equal("completed", (await fixture.InstanceAsync(parentId)).State);
    }

    [Fact]
    public async Task A_subject_expression_that_gives_no_record_fails_the_parents_step()
    {
        var (release, model) = await fixture.ActivateCopyWithFilesAsync(
            ("processes/nosubject-parent.json", Parent("f6c5b8a7-a0c3-416e-b29b-5d70ce416e01", "NoSubjectParent", "NoSubjectChild", """ "subject": "department", "next": "close" """)),
            ("processes/nosubject-child.json", """
                {
                  "id": "f6c5b8a7-a0c3-416e-b29b-5d70ce416e02",
                  "kind": "process",
                  "name": "NoSubjectChild",
                  "formatVersion": 1,
                  "entity": "Department",
                  "start": "finish",
                  "steps": [{ "name": "finish", "type": "end" }]
                }
                """));
        var request = await fixture.InsertRequestAsync(10, 2);
        var parentId = await fixture.StartAsync("NoSubjectParent", request, release, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal("failed", (await fixture.InstanceAsync(parentId)).State);
        var failure = Assert.Single(await fixture.HistoryAsync(parentId));
        Assert.Equal("The subject of the step 'fulfil' gave no record of 'Department'.", failure.Error);
        Assert.Equal(0, await ChildCountAsync("NoSubjectChild", model));
    }

    /// <summary>A process on Request named <paramref name="name"/> that runs <c>fulfil</c>, a sub-process step for <paramref name="child"/> with <paramref name="stepProperties"/>, then ends.</summary>
    private static string Parent(string id, string name, string child, string stepProperties) =>
        $$"""
        {
          "id": "{{id}}",
          "kind": "process",
          "name": "{{name}}",
          "formatVersion": 1,
          "entity": "Request",
          "start": "fulfil",
          "steps": [
            { "name": "fulfil", "type": "subProcess", "process": "{{child}}", {{stepProperties}} },
            {{CloseAndFinish}}
          ]
        }
        """;

    /// <summary>A process on Request named <paramref name="name"/> that ends at once in the end step <paramref name="end"/>, with <paramref name="properties"/> before its <c>start</c>.</summary>
    private static string Child(string id, string name, string properties, string end) =>
        $$"""
        {
          "id": "{{id}}",
          "kind": "process",
          "name": "{{name}}",
          "formatVersion": 1,
          "entity": "Request",
          {{properties}}
          "start": "{{end}}",
          "steps": [{ "name": "{{end}}", "type": "end" }]
        }
        """;

    private async Task<Guid> ChildIdAsync(string process, ApplicationModel model)
    {
        Assert.True(model.TryGetProcess(process, out var found));
        return (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_instances WHERE process_id = @process", ("process", found.Id)))!;
    }

    private Task<long> ChildCountAsync(string process, ApplicationModel model)
    {
        Assert.True(model.TryGetProcess(process, out var found));
        return fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", found.Id));
    }
}
