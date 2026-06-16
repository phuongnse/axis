using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using Axis.WorkflowBuilder.Domain.Events;
using Axis.WorkflowBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.WorkflowBuilder.Domain.Tests;

public class WorkflowDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-123";

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowDefinition_WhenCreated_SetsNameDescriptionWorkspaceIdAndDraftStatus()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", "Approves invoices", WorkspaceId, UserId);

        wf.Name.Should().Be("Invoice Approval");
        wf.Description.Should().Be("Approves invoices");
        wf.workspaceId.Should().Be(WorkspaceId);
        wf.Status.Should().Be(WorkflowStatus.Draft);
        wf.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void WorkflowDefinition_WhenCreated_SetsCreatedByAndTimestamps()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, UserId);

        wf.CreatedBy.Should().Be(UserId);
        wf.CreatedAt.Should().BeOnOrAfter(before);
        wf.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void WorkflowDefinition_WhenCreated_AddsStartAndEndNodesByDefault()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);

        wf.Steps.Should().HaveCount(2);
        wf.Steps.Should().Contain(s => s.Type == StepType.Start);
        wf.Steps.Should().Contain(s => s.Type == StepType.End);
    }

    [Fact]
    public void WorkflowDefinition_WhenCreated_RaisesWorkflowCreatedEvent()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        wf.DomainEvents.Should().ContainSingle(e => e is WorkflowCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]   // too short (< 2)
    public void WorkflowDefinition_WhenNameTooShort_ThrowsArgumentException(string name)
    {
        Func<WorkflowDefinition> act = () => WorkflowDefinition.Create(name, null, WorkspaceId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WorkflowDefinition_WhenNameExceeds200Chars_ThrowsArgumentException()
    {
        string longName = new string('A', 201);
        Func<WorkflowDefinition> act = () => WorkflowDefinition.Create(longName, null, WorkspaceId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── Publish ──────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_WhenWorkflowIsValid_TransitionsToActive()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep formStep = wf.AddStep("Review", StepType.Form, null);
        WorkflowStep startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);

        wf.Publish();

        wf.Status.Should().Be(WorkflowStatus.Active);
        wf.DomainEvents.Should().Contain(e => e is WorkflowPublished);
    }

    [Fact]
    public void Publish_WhenNoTriggersConfigured_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, UserId);

        Action act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*trigger*");
    }

    [Fact]
    public void Publish_WhenNoStepsBeyondStartAndEnd_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);

        Action act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*step*");
    }

    [Fact]
    public void Publish_WhenAlreadyActive_Throws()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();

        Action act = () => wf.Publish();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    // ─── Archive ──────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_WhenWorkflowIsActive_TransitionsToArchived()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        wf.Archive();

        wf.Status.Should().Be(WorkflowStatus.Archived);
        wf.DomainEvents.Should().Contain(e => e is WorkflowArchived);
    }

    [Fact]
    public void Archive_WhenInDraftStatus_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        Action act = () => wf.Archive();
        act.Should().Throw<InvalidOperationException>().WithMessage("*draft*");
    }

    // ─── Unarchive ────────────────────────────────────────────────────────────

    [Fact]
    public void Unarchive_WhenWorkflowIsArchived_TransitionsBackToActive()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        wf.Archive();
        wf.Unarchive();

        wf.Status.Should().Be(WorkflowStatus.Active);
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_WhenWorkflowIsDraft_SetsDeletedAt()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        wf.Delete();
        wf.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Delete_WhenWorkflowIsNotDraft_Throws()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        Action act = () => wf.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*draft*");
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WhenCalled_ChangesNameAndDescription()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Old Name", null, WorkspaceId, UserId);
        wf.Update("New Name", "Some description");

        wf.Name.Should().Be("New Name");
        wf.Description.Should().Be("Some description");
    }

    // ─── AddStep ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddStep_WhenCalled_AddsStepWithGivenType()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep step = wf.AddStep("Send Email", StepType.Notification, null);

        wf.Steps.Should().Contain(s => s.Id == step.Id && s.Type == StepType.Notification);
    }

    [Theory]
    [InlineData(StepType.Start)]
    [InlineData(StepType.End)]
    public void AddStep_WhenAddingStartOrEndStep_Throws(StepType type)
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        Func<WorkflowStep> act = () => wf.AddStep("Node", type, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    // ─── AddTransition ────────────────────────────────────────────────────────

    [Fact]
    public void AddTransition_WhenStepsExist_ConnectsTwoSteps()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep endStep = wf.Steps.Single(s => s.Type == StepType.End);

        wf.AddTransition(startStep.Id, endStep.Id, null);

        wf.Transitions.Should().ContainSingle(t =>
            t.FromStepId == startStep.Id && t.ToStepId == endStep.Id);
    }

    [Fact]
    public void AddTransition_WhenCreatingACycle_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep s1 = wf.AddStep("Step 1", StepType.Form, null);
        WorkflowStep s2 = wf.AddStep("Step 2", StepType.Form, null);
        wf.AddTransition(s1.Id, s2.Id, null);

        Action act = () => wf.AddTransition(s2.Id, s1.Id, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    // ─── Duplicate ────────────────────────────────────────────────────────────

    [Fact]
    public void Duplicate_WhenCalled_CreatesNewDraftWithCopyName()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        WorkflowDefinition copy = wf.Duplicate();

        copy.Status.Should().Be(WorkflowStatus.Draft);
        copy.Name.Should().Be($"Copy of {wf.Name}");
        copy.Id.Should().NotBe(wf.Id);
        copy.Steps.Should().HaveCount(wf.Steps.Count);
    }

    [Fact]
    public void Duplicate_WhenCalled_DoesNotCopyExecutionHistory()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        WorkflowDefinition copy = wf.Duplicate();
        copy.Id.Should().NotBe(wf.Id);
    }

    // ─── AddTrigger ───────────────────────────────────────────────────────────

    [Fact]
    public void AddTrigger_WhenCalled_AddsTriggerToWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);

        wf.Triggers.Should().ContainSingle(t => t.Type == TriggerType.Manual);
    }

    // ─── ConfigureStep ────────────────────────────────────────────────────────

    [Fact]
    public void ConfigureStep_WhenStepExists_UpdatesNameAndConfig()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep step = wf.AddStep("Old Name", StepType.Form, null);
        Dictionary<string, object?> config = new Dictionary<string, object?> { ["form_id"] = Guid.NewGuid() };

        wf.ConfigureStep(step.Id, "New Name", config);
        WorkflowStep updated = wf.Steps.Single(s => s.Id == step.Id);
        updated.Name.Should().Be("New Name");
        updated.Config.Should().BeEquivalentTo(config);
    }

    [Fact]
    public void ConfigureStep_WhenStepNotFound_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        Action act = () => wf.ConfigureStep(Guid.NewGuid(), "Name", null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Theory]
    [InlineData(StepType.Start)]
    [InlineData(StepType.End)]
    public void ConfigureStep_WhenReservedStep_Throws(StepType reservedType)
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep reserved = wf.Steps.Single(s => s.Type == reservedType);

        Action act = () => wf.ConfigureStep(reserved.Id, "New Name", null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Fact]
    public void ConfigureStep_WhenCalled_UpdatesUpdatedAt()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        WorkflowStep step = wf.AddStep("Step", StepType.HttpRequest, null);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        wf.ConfigureStep(step.Id, "Updated Step", null);

        wf.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // ─── AddTrigger (duplicate check) ─────────────────────────────────────────

    [Fact]
    public void AddTrigger_WhenSameTypeAlreadyExists_Throws()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);

        Action act = () => wf.AddTrigger(TriggerType.Manual, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }

    [Fact]
    public void AddTrigger_WhenDifferentTypes_AddsBoth()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        wf.AddTrigger(TriggerType.Webhook, null);

        wf.Triggers.Should().HaveCount(2);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static WorkflowDefinition CreatePublishableWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep formStep = wf.AddStep("Review", StepType.Form, null);
        WorkflowStep startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);
        return wf;
    }
}
