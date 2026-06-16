using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Handlers;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Handlers;

public class ExecuteConditionStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<ExecuteConditionStepHandler> _logger = Substitute.For<ILogger<ExecuteConditionStepHandler>>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private ExecuteConditionStepHandler CreateHandler() => new(_execRepo, _uow, _dispatcher, _logger);

    private static WorkflowExecution CreatePendingExecution()
        => WorkflowExecution.Create(WorkflowId, TeamAccountId, TriggerType.Manual, null, new Dictionary<string, object?>());

    // Builds a simple branch list: one named branch + one condition expression
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ApprovedBranches()
        => new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["label"] = "approved",
                ["expression"] = new Dictionary<string, object?>
                {
                    ["type"] = "==",
                    ["field"] = "status",
                    ["value"] = "approved"
                }
            },
            new Dictionary<string, object?>
            {
                ["label"] = "rejected",
                ["expression"] = new Dictionary<string, object?>
                {
                    ["type"] = "==",
                    ["field"] = "status",
                    ["value"] = "rejected"
                }
            }
        };

    [Fact]
    public async Task HandleAsync_WhenBranchMatches_SkipsRejectedBranchStepsAndDispatchesStepCompleted()
    {
        WorkflowExecution execution = CreatePendingExecution();

        // Build workflow: Condition → [ApprovedStep, RejectedStep]
        Guid condDefId = Guid.NewGuid();
        Guid approvedDefId = Guid.NewGuid();
        Guid rejectedDefId = Guid.NewGuid();

        ExecutionStep condStep = execution.AddStep(condDefId, "Condition", StepType.Condition, 0);
        ExecutionStep approvedStep = execution.AddStep(approvedDefId, "Approved", StepType.Form, 1);
        ExecutionStep rejectedStep = execution.AddStep(rejectedDefId, "Rejected", StepType.Form, 2);

        execution.Start();
        execution.StartStep(condStep.Id, execution.Context);

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        List<ConditionTransition> transitions = new()
        {
            new ConditionTransition(condDefId, approvedDefId, "approved"),
            new ConditionTransition(condDefId, rejectedDefId, "rejected")
        };

        // Context has status=approved → "approved" branch should be selected
        Dictionary<string, object?> context = new() { ["status"] = "approved" };

        IReadOnlyDictionary<string, object?> config = new Dictionary<string, object?>
        {
            ["branches"] = ApprovedBranches()
        };

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                execution.Id, condStep.Id, TeamAccountId,
                config, context,
                new List<Guid> { condDefId, approvedDefId, rejectedDefId },
                transitions),
            CancellationToken.None);

        // Rejected branch step should be skipped
        rejectedStep.Status.Should().Be(StepExecutionStatus.Skipped);
        // Approved branch step remains Pending (will be picked up by next ExecuteNextStepMessage)
        approvedStep.Status.Should().Be(StepExecutionStatus.Pending);

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m =>
                m.ExecutionId == execution.Id &&
                m.StepId == condStep.Id &&
                (string?)m.Output["selectedBranch"] == "approved"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoBranchesConfigured_DispatchesStepFailedMessage()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid condDefId = Guid.NewGuid();
        ExecutionStep condStep = execution.AddStep(condDefId, "Condition", StepType.Condition, 0);
        execution.Start();
        execution.StartStep(condStep.Id, execution.Context);

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        // Empty config — no branches key
        IReadOnlyDictionary<string, object?> config = new Dictionary<string, object?>();

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                execution.Id, condStep.Id, TeamAccountId,
                config, execution.Context,
                new List<Guid>(), new List<ConditionTransition>()),
            CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepFailedMessage>(m => m.StepId == condStep.Id),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<StepCompletedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNoBranchMatches_DispatchesStepFailedMessage()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid condDefId = Guid.NewGuid();
        ExecutionStep condStep = execution.AddStep(condDefId, "Condition", StepType.Condition, 0);
        execution.Start();
        execution.StartStep(condStep.Id, execution.Context);

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        IReadOnlyDictionary<string, object?> config = new Dictionary<string, object?>
        {
            ["branches"] = ApprovedBranches()
        };

        // Context has status=pending — neither "approved" nor "rejected" branch matches
        Dictionary<string, object?> context = new() { ["status"] = "pending" };

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                execution.Id, condStep.Id, TeamAccountId,
                config, context,
                new List<Guid>(), new List<ConditionTransition>()),
            CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepFailedMessage>(m => m.StepId == condStep.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsWithoutAction()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid condDefId = Guid.NewGuid();
        ExecutionStep condStep = execution.AddStep(condDefId, "Condition", StepType.Condition, 0);
        execution.Start();
        execution.StartStep(condStep.Id, execution.Context);
        execution.CompleteStep(condStep.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                execution.Id, condStep.Id, TeamAccountId,
                null, execution.Context,
                new List<Guid>(), new List<ConditionTransition>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                Guid.NewGuid(), Guid.NewGuid(), TeamAccountId,
                null, new Dictionary<string, object?>(),
                new List<Guid>(), new List<ConditionTransition>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyException_ExitsGracefullyWithoutDispatch()
    {
        // The losing worker must not dispatch StepCompleted — the winning instance already did.
        WorkflowExecution execution = CreatePendingExecution();
        Guid condDefId = Guid.NewGuid();
        Guid approvedDefId = Guid.NewGuid();
        ExecutionStep condStep = execution.AddStep(condDefId, "Condition", StepType.Condition, 0);
        execution.AddStep(approvedDefId, "Approved", StepType.Form, 1);
        execution.Start();
        execution.StartStep(condStep.Id, execution.Context);

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        List<ConditionTransition> transitions = new()
        {
            new ConditionTransition(condDefId, approvedDefId, "approved")
        };
        IReadOnlyDictionary<string, object?> config = new Dictionary<string, object?>
        {
            ["branches"] = ApprovedBranches()
        };
        Dictionary<string, object?> context = new() { ["status"] = "approved" };

        await CreateHandler().HandleAsync(
            new ExecuteConditionStepMessage(
                execution.Id, condStep.Id, TeamAccountId,
                config, context,
                new List<Guid> { condDefId, approvedDefId },
                transitions),
            CancellationToken.None);

        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<StepCompletedMessage>(), Arg.Any<CancellationToken>());
    }
}
