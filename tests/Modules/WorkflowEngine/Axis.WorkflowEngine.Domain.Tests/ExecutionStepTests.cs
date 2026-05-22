using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;

namespace Axis.WorkflowEngine.Domain.Tests;

public class ExecutionStepTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ExecutionId = Guid.NewGuid();
    private static readonly Guid StepDefinitionId = Guid.NewGuid();

    private static ExecutionStep CreatePending(string name = "Send Email", StepType type = StepType.Notification, int order = 1) =>
        ExecutionStep.Create(ExecutionId, OrgId, StepDefinitionId, name, type, order);

    private static IReadOnlyDictionary<string, object?> SomeContext() =>
        new Dictionary<string, object?> { ["key"] = "value" };

    // ─── Create guards ────────────────────────────────────────────────────────

    [Fact]
    public void Create_WhenExecutionIdIsEmpty_Throws()
    {
        Action act = () => ExecutionStep.Create(Guid.Empty, OrgId, StepDefinitionId, "Name", StepType.Form, 0);
        act.Should().Throw<ArgumentException>().WithParameterName("executionId");
    }

    [Fact]
    public void Create_WhenOrganizationIdIsEmpty_Throws()
    {
        Action act = () => ExecutionStep.Create(ExecutionId, Guid.Empty, StepDefinitionId, "Name", StepType.Form, 0);
        act.Should().Throw<ArgumentException>().WithParameterName("organizationId");
    }

    [Fact]
    public void Create_WhenStepDefinitionIdIsEmpty_Throws()
    {
        Action act = () => ExecutionStep.Create(ExecutionId, OrgId, Guid.Empty, "Name", StepType.Form, 0);
        act.Should().Throw<ArgumentException>().WithParameterName("stepDefinitionId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsBlank_Throws(string name)
    {
        Action act = () => ExecutionStep.Create(ExecutionId, OrgId, StepDefinitionId, name, StepType.Form, 0);
        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void Create_WhenDisplayOrderIsNegative_Throws()
    {
        Action act = () => ExecutionStep.Create(ExecutionId, OrgId, StepDefinitionId, "Name", StepType.Form, -1);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("displayOrder");
    }

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void ExecutionStep_WhenCreated_SetsPropertiesAndPendingStatus()
    {
        var step = CreatePending("Approve Request", StepType.Form, 3);

        step.ExecutionId.Should().Be(ExecutionId);
        step.OrganizationId.Should().Be(OrgId);
        step.StepDefinitionId.Should().Be(StepDefinitionId);
        step.Name.Should().Be("Approve Request");
        step.StepType.Should().Be(StepType.Form);
        step.DisplayOrder.Should().Be(3);
        step.Status.Should().Be(StepExecutionStatus.Pending);
        step.StartedAt.Should().BeNull();
        step.CompletedAt.Should().BeNull();
        step.InputSnapshot.Should().BeNull();
        step.OutputSnapshot.Should().BeNull();
        step.ErrorDetails.Should().BeNull();
    }

    [Fact]
    public void ExecutionStep_WhenCreated_SetsCreatedAt()
    {
        var before = DateTimeOffset.UtcNow;
        var step = CreatePending();

        step.CreatedAt.Should().BeOnOrAfter(before);
    }

    // ─── Start ────────────────────────────────────────────────────────────────

    [Fact]
    public void Start_WhenPending_TransitionsToRunningAndRecordsSnapshot()
    {
        var step = CreatePending();
        var input = SomeContext();
        step.Start(input);

        step.Status.Should().Be(StepExecutionStatus.Running);
        step.StartedAt.Should().NotBeNull();
        step.InputSnapshot.Should().BeEquivalentTo(input);
    }

    [Theory]
    [InlineData(StepExecutionStatus.Running)]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Start_WhenNotPending_Throws(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        var act = () => step.Start(SomeContext());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending*");
    }

    // ─── Complete ─────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_WhenRunning_TransitionsToCompletedWithOutput()
    {
        var step = CreatePending();
        step.Start(SomeContext());
        var output = new Dictionary<string, object?> { ["result"] = 42 };
        step.Complete(output);

        step.Status.Should().Be(StepExecutionStatus.Completed);
        step.CompletedAt.Should().NotBeNull();
        step.OutputSnapshot.Should().BeEquivalentTo(output);
    }

    [Fact]
    public void Complete_WhenWaiting_TransitionsToCompleted()
    {
        var step = CreatePending(type: StepType.Form);
        step.Start(SomeContext());
        step.Wait();
        var output = new Dictionary<string, object?> { ["approved"] = true };
        step.Complete(output);

        step.Status.Should().Be(StepExecutionStatus.Completed);
    }

    [Theory]
    [InlineData(StepExecutionStatus.Pending)]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Complete_WhenNotRunningOrWaiting_Throws(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        var act = () => step.Complete(SomeContext());
        act.Should().Throw<InvalidOperationException>().WithMessage("*Running*");
    }

    // ─── Fail ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_WhenRunning_TransitionsToFailedWithErrorDetails()
    {
        var step = CreatePending();
        step.Start(SomeContext());
        step.Fail("Connection timeout after 5s");

        step.Status.Should().Be(StepExecutionStatus.Failed);
        step.CompletedAt.Should().NotBeNull();
        step.ErrorDetails.Should().Be("Connection timeout after 5s");
    }

    [Fact]
    public void Fail_WhenWaiting_TransitionsToFailedWithErrorDetails()
    {
        ExecutionStep step = CreatePending(type: StepType.Form);
        step.Start(SomeContext());
        step.Wait();
        step.Fail("Form step timed out");

        step.Status.Should().Be(StepExecutionStatus.Failed);
        step.CompletedAt.Should().NotBeNull();
        step.ErrorDetails.Should().Be("Form step timed out");
    }

    [Theory]
    [InlineData(StepExecutionStatus.Pending)]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Fail_WhenNotRunningOrWaiting_Throws(StepExecutionStatus status)
    {
        ExecutionStep step = CreatePending();
        BringToStatus(step, status);

        Action act = () => step.Fail("error");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Running or Waiting*");
    }

    // ─── Wait ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Wait_WhenRunning_TransitionsToWaiting()
    {
        var step = CreatePending(type: StepType.Form);
        step.Start(SomeContext());
        step.Wait();

        step.Status.Should().Be(StepExecutionStatus.Waiting);
    }

    [Theory]
    [InlineData(StepExecutionStatus.Pending)]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Wait_WhenNotRunning_Throws(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        var act = () => step.Wait();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Running*");
    }

    // ─── Skip ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Skip_WhenPending_TransitionsToSkipped()
    {
        var step = CreatePending(type: StepType.Condition);
        step.Skip("Branch condition not taken");

        step.Status.Should().Be(StepExecutionStatus.Skipped);
        step.CompletedAt.Should().NotBeNull();
        step.ErrorDetails.Should().Be("Branch condition not taken");
    }

    [Theory]
    [InlineData(StepExecutionStatus.Running)]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Skip_WhenNotPending_Throws(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        var act = () => step.Skip("reason");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Pending*");
    }

    // ─── Cancel ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(StepExecutionStatus.Pending)]
    [InlineData(StepExecutionStatus.Running)]
    [InlineData(StepExecutionStatus.Waiting)]
    public void Cancel_WhenNonTerminal_TransitionsToCancelled(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);
        step.Cancel();

        step.Status.Should().Be(StepExecutionStatus.Cancelled);
        step.CompletedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Skipped)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void Cancel_WhenAlreadyTerminal_Throws(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        var act = () => step.Cancel();
        act.Should().Throw<InvalidOperationException>().WithMessage("*cancel*");
    }

    // ─── Idempotency (US-093 edge case) ──────────────────────────────────────

    [Theory]
    [InlineData(StepExecutionStatus.Completed)]
    [InlineData(StepExecutionStatus.Failed)]
    [InlineData(StepExecutionStatus.Cancelled)]
    public void IsTerminal_WhenInTerminalState_ReturnsTrue(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        step.IsTerminal.Should().BeTrue();
    }

    [Theory]
    [InlineData(StepExecutionStatus.Pending)]
    [InlineData(StepExecutionStatus.Running)]
    [InlineData(StepExecutionStatus.Waiting)]
    [InlineData(StepExecutionStatus.Skipped)]
    public void IsTerminal_WhenNotInTerminalState_ReturnsFalse(StepExecutionStatus status)
    {
        var step = CreatePending();
        BringToStatus(step, status);

        step.IsTerminal.Should().BeFalse();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static void BringToStatus(ExecutionStep step, StepExecutionStatus target)
    {
        switch (target)
        {
            case StepExecutionStatus.Pending:
                break;
            case StepExecutionStatus.Running:
                step.Start(SomeContext());
                break;
            case StepExecutionStatus.Waiting:
                step.Start(SomeContext());
                step.Wait();
                break;
            case StepExecutionStatus.Completed:
                step.Start(SomeContext());
                step.Complete(SomeContext());
                break;
            case StepExecutionStatus.Failed:
                step.Start(SomeContext());
                step.Fail("error");
                break;
            case StepExecutionStatus.Skipped:
                step.Skip("reason");
                break;
            case StepExecutionStatus.Cancelled:
                step.Cancel();
                break;
        }
    }
}
