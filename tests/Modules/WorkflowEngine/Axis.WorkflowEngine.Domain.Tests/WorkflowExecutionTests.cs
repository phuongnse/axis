using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.Events;
using FluentAssertions;

namespace Axis.WorkflowEngine.Domain.Tests;

public class WorkflowExecutionTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid TriggeredBy = Guid.NewGuid();
    private static readonly Guid StepDefId = Guid.NewGuid();

    private static IReadOnlyDictionary<string, object?> EmptyInput() =>
        new Dictionary<string, object?>();

    private static IReadOnlyDictionary<string, object?> SomeData() =>
        new Dictionary<string, object?> { ["key"] = "val" };

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowExecution_WhenCreated_SetsWorkflowWorkspaceTriggerAndPendingStatus()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());

        exec.WorkflowDefinitionId.Should().Be(WorkflowId);
        exec.workspaceId.Should().Be(WorkspaceId);
        exec.TriggerType.Should().Be(TriggerType.Manual);
        exec.TriggeredByUserId.Should().Be(TriggeredBy);
        exec.Status.Should().Be(ExecutionStatus.Pending);
        exec.RetryOfExecutionId.Should().BeNull();
    }

    [Fact]
    public void WorkflowExecution_WhenCreated_RaisesExecutionStartedEvent()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.DomainEvents.Should().ContainSingle(e => e is ExecutionStarted);
    }

    // ─── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WhenExecutionIsPending_TransitionsToRunning()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        exec.Status.Should().Be(ExecutionStatus.Running);
        exec.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void Start_WhenNotInPendingStatus_Throws()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        Action act = () => exec.Start();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    // ─── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_WhenExecutionIsRunning_TransitionsToCompleted()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();
        exec.Complete();

        exec.Status.Should().Be(ExecutionStatus.Completed);
        exec.CompletedAt.Should().NotBeNull();
        exec.DomainEvents.Should().Contain(e => e is ExecutionCompleted);
    }

    [Fact]
    public void Complete_WhenNotRunning_Throws()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        Action act = () => exec.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*running*");
    }

    // ─── Fail ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_WhenExecutionIsRunning_TransitionsToFailedWithErrorMessage()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();
        exec.Fail("Step X: connection timeout");

        exec.Status.Should().Be(ExecutionStatus.Failed);
        exec.ErrorMessage.Should().Be("Step X: connection timeout");
        exec.DomainEvents.Should().Contain(e => e is ExecutionFailed);
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_WhenRunning_TransitionsToCancelled()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();
        exec.Cancel();

        exec.Status.Should().Be(ExecutionStatus.Cancelled);
        exec.DomainEvents.Should().Contain(e => e is ExecutionCancelled);
    }

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Cancel();
        exec.Status.Should().Be(ExecutionStatus.Cancelled);
    }

    [Theory]
    [InlineData(ExecutionStatus.Completed)]
    [InlineData(ExecutionStatus.Failed)]
    [InlineData(ExecutionStatus.Cancelled)]
    public void Cancel_WhenExecutionIsInTerminalState_Throws(ExecutionStatus terminal)
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        // Bring to terminal state
        if (terminal == ExecutionStatus.Completed) exec.Complete();
        else if (terminal == ExecutionStatus.Failed) exec.Fail("error");
        else exec.Cancel();

        Action act = () => exec.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*cancel*");
    }

    // ─── Steps ────────────────────────────────────────────────────────────────

    [Fact]
    public void AddStep_WhenStepIsValid_AddsStepToCollection()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.AddStep(StepDefId, "Send Email", StepType.Notification, 0);

        exec.Steps.Should().HaveCount(1);
        exec.Steps[0].Name.Should().Be("Send Email");
        exec.Steps[0].DisplayOrder.Should().Be(0);
        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Pending);
    }

    [Fact]
    public void StartStep_WhenStepIsPending_TransitionsStepToRunning()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Send Email", StepType.Notification, 0);
        exec.StartStep(step.Id, SomeData());

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Running);
    }

    [Fact]
    public void CompleteStep_WhenStepIsRunning_TransitionsStepToCompletedAndRaisesEvent()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Send Email", StepType.Notification, 0);
        exec.StartStep(step.Id, SomeData());
        exec.ClearDomainEvents();

        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?> { ["result"] = "ok" };
        exec.CompleteStep(step.Id, output);

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Completed);
        ExecutionStepCompleted evt = exec.DomainEvents.OfType<ExecutionStepCompleted>().Single();
        evt.ExecutionId.Should().Be(exec.Id);
        evt.StepId.Should().Be(step.Id);
        evt.workspaceId.Should().Be(WorkspaceId);
        evt.Output.Should().BeEquivalentTo(output);
    }

    [Fact]
    public void FailStep_WhenStepIsRunning_TransitionsStepToFailedAndRaisesEvent()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Send Email", StepType.Notification, 0);
        exec.StartStep(step.Id, SomeData());
        exec.ClearDomainEvents();

        exec.FailStep(step.Id, "Connection timeout");

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Failed);
        ExecutionStepFailed evt = exec.DomainEvents.OfType<ExecutionStepFailed>().Single();
        evt.ExecutionId.Should().Be(exec.Id);
        evt.StepId.Should().Be(step.Id);
        evt.workspaceId.Should().Be(WorkspaceId);
        evt.ErrorDetails.Should().Be("Connection timeout");
    }

    [Fact]
    public void WaitStep_WhenStepIsRunning_TransitionsStepToWaiting()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Approval", StepType.Form, 0);
        exec.StartStep(step.Id, SomeData());
        exec.WaitStep(step.Id);

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Waiting);
    }

    [Fact]
    public void SkipStep_WhenStepIsPending_TransitionsStepToSkipped()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Condition Branch", StepType.Condition, 0);
        exec.SkipStep(step.Id, "Branch not taken");

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Skipped);
        exec.Steps[0].ErrorDetails.Should().Be("Branch not taken");
    }

    [Fact]
    public void CancelStep_WhenStepIsRunning_TransitionsStepToCancelled()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        ExecutionStep step = exec.AddStep(StepDefId, "Send Email", StepType.Notification, 0);
        exec.StartStep(step.Id, SomeData());
        exec.CancelStep(step.Id);

        exec.Steps[0].Status.Should().Be(StepExecutionStatus.Cancelled);
    }

    [Fact]
    public void StepOperation_WhenStepIdNotFound_Throws()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        Guid missing = Guid.NewGuid();

        Action act = () => exec.StartStep(missing, SomeData());

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    // ─── Retry ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateRetry_WhenExecutionHasFailed_CreatesNewExecutionLinkedToOriginal()
    {
        WorkflowExecution original = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        original.Start();
        original.Fail("error");
        WorkflowExecution retry = original.CreateRetry(TriggeredBy);

        retry.RetryOfExecutionId.Should().Be(original.Id);
        retry.Status.Should().Be(ExecutionStatus.Pending);
        retry.WorkflowDefinitionId.Should().Be(original.WorkflowDefinitionId);
    }

    [Fact]
    public void CreateRetry_WhenExecutionIsNotFailed_Throws()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        Func<WorkflowExecution> act = () => exec.CreateRetry(TriggeredBy);
        act.Should().Throw<InvalidOperationException>().WithMessage("*failed*");
    }
}
