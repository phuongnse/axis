using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.WorkflowBuilder.Domain.Tests;

public class WorkflowDefinitionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_sets_name_description_orgId_and_Draft_status()
    {
        var wf = WorkflowDefinition.Create("Invoice Approval", "Approves invoices", OrgId, UserId);

        wf.Name.Should().Be("Invoice Approval");
        wf.Description.Should().Be("Approves invoices");
        wf.OrganizationId.Should().Be(OrgId);
        wf.Status.Should().Be(WorkflowStatus.Draft);
        wf.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_sets_CreatedBy_and_DateTimeOffset_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);

        wf.CreatedBy.Should().Be(UserId);
        wf.CreatedAt.Should().BeOnOrAfter(before);
        wf.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Create_adds_Start_and_End_nodes_by_default()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);

        wf.Steps.Should().HaveCount(2);
        wf.Steps.Should().Contain(s => s.Type == StepType.Start);
        wf.Steps.Should().Contain(s => s.Type == StepType.End);
    }

    [Fact]
    public void Create_raises_WorkflowCreated_event()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        wf.DomainEvents.Should().ContainSingle(e => e is WorkflowCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]   // too short (< 2)
    public void Create_throws_when_name_too_short(string name)
    {
        var act = () => WorkflowDefinition.Create(name, null, OrgId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_when_name_exceeds_200_chars()
    {
        var longName = new string('A', 201);
        var act = () => WorkflowDefinition.Create(longName, null, OrgId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── Publish ──────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_transitions_to_Active_when_valid()
    {
        var wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        var formStep = wf.AddStep("Review", StepType.Form, null);
        var startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        var endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);

        wf.Publish();

        wf.Status.Should().Be(WorkflowStatus.Active);
        wf.DomainEvents.Should().Contain(e => e is WorkflowPublished);
    }

    [Fact]
    public void Publish_throws_when_no_triggers_configured()
    {
        var wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);

        var act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*trigger*");
    }

    [Fact]
    public void Publish_throws_when_no_steps_beyond_start_and_end()
    {
        var wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);

        var act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*step*");
    }

    [Fact]
    public void Publish_throws_when_already_active()
    {
        var wf = CreatePublishableWorkflow();
        wf.Publish();

        var act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    // ─── Archive ──────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_transitions_to_Archived()
    {
        var wf = CreatePublishableWorkflow();
        wf.Publish();
        wf.Archive();

        wf.Status.Should().Be(WorkflowStatus.Archived);
        wf.DomainEvents.Should().Contain(e => e is WorkflowArchived);
    }

    [Fact]
    public void Archive_throws_when_in_Draft_status()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        var act = () => wf.Archive();
        act.Should().Throw<InvalidOperationException>().WithMessage("*draft*");
    }

    // ─── Unarchive ────────────────────────────────────────────────────────────

    [Fact]
    public void Unarchive_transitions_archived_back_to_Active()
    {
        var wf = CreatePublishableWorkflow();
        wf.Publish();
        wf.Archive();
        wf.Unarchive();

        wf.Status.Should().Be(WorkflowStatus.Active);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_changes_name_and_description()
    {
        var wf = WorkflowDefinition.Create("Old Name", null, OrgId, UserId);
        wf.Update("New Name", "Some description");

        wf.Name.Should().Be("New Name");
        wf.Description.Should().Be("Some description");
    }

    // ─── AddStep ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddStep_adds_a_step_with_given_type()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        var step = wf.AddStep("Send Email", StepType.Notification, null);

        wf.Steps.Should().Contain(s => s.Id == step.Id && s.Type == StepType.Notification);
    }

    [Theory]
    [InlineData(StepType.Start)]
    [InlineData(StepType.End)]
    public void AddStep_throws_when_adding_Start_or_End_step(StepType type)
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        var act = () => wf.AddStep("Node", type, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    // ─── AddTransition ────────────────────────────────────────────────────────

    [Fact]
    public void AddTransition_connects_two_steps()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        var startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        var endStep = wf.Steps.Single(s => s.Type == StepType.End);

        wf.AddTransition(startStep.Id, endStep.Id, null);

        wf.Transitions.Should().ContainSingle(t =>
            t.FromStepId == startStep.Id && t.ToStepId == endStep.Id);
    }

    [Fact]
    public void AddTransition_throws_when_creating_a_cycle()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        var s1 = wf.AddStep("Step 1", StepType.Form, null);
        var s2 = wf.AddStep("Step 2", StepType.Form, null);
        wf.AddTransition(s1.Id, s2.Id, null);

        var act = () => wf.AddTransition(s2.Id, s1.Id, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    // ─── Duplicate ────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_creates_new_draft_with_copy_name()
    {
        var wf = CreatePublishableWorkflow();
        wf.Publish();

        var copy = wf.Duplicate();

        copy.Status.Should().Be(WorkflowStatus.Draft);
        copy.Name.Should().Be($"Copy of {wf.Name}");
        copy.Id.Should().NotBe(wf.Id);
        copy.Steps.Should().HaveCount(wf.Steps.Count);
    }

    [Fact]
    public void Duplicate_does_not_copy_execution_history()
    {
        var wf = CreatePublishableWorkflow();
        var copy = wf.Duplicate();
        copy.Id.Should().NotBe(wf.Id);
    }

    // ─── AddTrigger ───────────────────────────────────────────────────────────

    [Fact]
    public void AddTrigger_adds_trigger_to_workflow()
    {
        var wf = WorkflowDefinition.Create("My Workflow", null, OrgId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);

        wf.Triggers.Should().ContainSingle(t => t.Type == TriggerType.Manual);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static WorkflowDefinition CreatePublishableWorkflow()
    {
        var wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        var formStep = wf.AddStep("Review", StepType.Form, null);
        var startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        var endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);
        return wf;
    }
}
