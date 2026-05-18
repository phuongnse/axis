using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.Events;
using FluentAssertions;

namespace Axis.WorkflowEngine.Domain.Tests;

public class WorkflowExecutionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid TriggeredBy = Guid.NewGuid();

    private static IReadOnlyDictionary<string, object?> EmptyInput() =>
        new Dictionary<string, object?>();

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowExecution_WhenCreated_SetsWorkflowOrgTriggerAndPendingStatus()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());

        exec.WorkflowDefinitionId.Should().Be(WorkflowId);
        exec.OrganizationId.Should().Be(OrgId);
        exec.TriggerType.Should().Be(TriggerType.Manual);
        exec.TriggeredByUserId.Should().Be(TriggeredBy);
        exec.Status.Should().Be(ExecutionStatus.Pending);
        exec.RetryOfExecutionId.Should().BeNull();
    }

    [Fact]
    public void WorkflowExecution_WhenCreated_RaisesExecutionStartedEvent()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.DomainEvents.Should().ContainSingle(e => e is ExecutionStarted);
    }

    // ─── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WhenExecutionIsPending_TransitionsToRunning()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        exec.Status.Should().Be(ExecutionStatus.Running);
        exec.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public void Start_WhenNotInPendingStatus_Throws()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        var act = () => exec.Start();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    // ─── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_WhenExecutionIsRunning_TransitionsToCompleted()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();
        exec.Complete();

        exec.Status.Should().Be(ExecutionStatus.Completed);
        exec.CompletedAt.Should().NotBeNull();
        exec.DomainEvents.Should().Contain(e => e is ExecutionCompleted);
    }

    [Fact]
    public void Complete_WhenNotRunning_Throws()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        var act = () => exec.Complete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*running*");
    }

    // ─── Fail ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_WhenExecutionIsRunning_TransitionsToFailedWithErrorMessage()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
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
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();
        exec.Cancel();

        exec.Status.Should().Be(ExecutionStatus.Cancelled);
        exec.DomainEvents.Should().Contain(e => e is ExecutionCancelled);
    }

    [Fact]
    public void Cancel_WhenPending_TransitionsToCancelled()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Cancel();
        exec.Status.Should().Be(ExecutionStatus.Cancelled);
    }

    [Theory]
    [InlineData(ExecutionStatus.Completed)]
    [InlineData(ExecutionStatus.Failed)]
    [InlineData(ExecutionStatus.Cancelled)]
    public void Cancel_WhenExecutionIsInTerminalState_Throws(ExecutionStatus terminal)
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        // Bring to terminal state
        if (terminal == ExecutionStatus.Completed) exec.Complete();
        else if (terminal == ExecutionStatus.Failed) exec.Fail("error");
        else exec.Cancel();

        var act = () => exec.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*cancel*");
    }

    // ─── Retry ────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateRetry_WhenExecutionHasFailed_CreatesNewExecutionLinkedToOriginal()
    {
        var original = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        original.Start();
        original.Fail("error");

        var retry = original.CreateRetry(TriggeredBy);

        retry.RetryOfExecutionId.Should().Be(original.Id);
        retry.Status.Should().Be(ExecutionStatus.Pending);
        retry.WorkflowDefinitionId.Should().Be(original.WorkflowDefinitionId);
    }

    [Fact]
    public void CreateRetry_WhenExecutionIsNotFailed_Throws()
    {
        var exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, TriggeredBy, EmptyInput());
        exec.Start();

        var act = () => exec.CreateRetry(TriggeredBy);
        act.Should().Throw<InvalidOperationException>().WithMessage("*failed*");
    }
}
