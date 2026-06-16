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

public class ExecuteFormStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<ExecuteFormStepHandler> _logger = Substitute.For<ILogger<ExecuteFormStepHandler>>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid FormId = Guid.NewGuid();

    private ExecuteFormStepHandler CreateHandler() => new(_execRepo, _uow, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakeRunningFormStep()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Form", StepType.Form, 0);
        exec.Start();
        exec.StartStep(step.Id, exec.Context);
        return (exec, step);
    }

    private static IReadOnlyDictionary<string, object?> ValidConfig()
        => new Dictionary<string, object?>
        {
            ["formId"] = FormId.ToString(),
            ["assignee"] = "{{context.assigned_to}}",
            ["timeoutHours"] = 48
        };

    [Fact]
    public async Task HandleAsync_WithValidConfig_SetsStepWaitingAndRaisesFormStepReached()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningFormStep();
        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteFormStepMessage(execution.Id, step.Id, WorkspaceId, WorkflowId, ValidConfig(), execution.Context),
            CancellationToken.None);

        step.Status.Should().Be(StepExecutionStatus.Waiting);
        execution.DomainEvents.Should().ContainSingle(e => e is Domain.Events.FormStepReached);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithMissingFormId_FailsStepAndExecution()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningFormStep();
        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);

        IReadOnlyDictionary<string, object?> badConfig = new Dictionary<string, object?>
        {
            ["assignee"] = "user"
            // formId intentionally missing
        };

        await CreateHandler().HandleAsync(
            new ExecuteFormStepMessage(execution.Id, step.Id, WorkspaceId, WorkflowId, badConfig, execution.Context),
            CancellationToken.None);

        step.Status.Should().Be(StepExecutionStatus.Failed);
        execution.Status.Should().Be(ExecutionStatus.Failed);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepAlreadyWaiting_SkipsWithoutAction()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningFormStep();
        // Put step in Waiting state
        execution.WaitStep(step.Id);
        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteFormStepMessage(execution.Id, step.Id, WorkspaceId, WorkflowId, ValidConfig(), execution.Context),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteFormStepMessage(Guid.NewGuid(), Guid.NewGuid(), WorkspaceId, WorkflowId, ValidConfig(), new Dictionary<string, object?>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyException_ExitsGracefully()
    {
        // FormBuilder will receive no FormStepReached event because UoW rolled back — the winning
        // worker already published it. This handler must exit without rethrowing.
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningFormStep();
        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        Func<Task> act = () => CreateHandler().HandleAsync(
            new ExecuteFormStepMessage(execution.Id, step.Id, WorkspaceId, WorkflowId, ValidConfig(), execution.Context),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
