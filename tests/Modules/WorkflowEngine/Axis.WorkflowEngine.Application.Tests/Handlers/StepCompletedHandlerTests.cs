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

public class StepCompletedHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<StepCompletedHandler> _logger = Substitute.For<ILogger<StepCompletedHandler>>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private StepCompletedHandler CreateHandler() => new(_execRepo, _uow, _dispatcher, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakeRunningExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Form", StepType.Form, 0);
        exec.Start();
        exec.StartStep(step.Id, exec.Context);
        return (exec, step);
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsRunning_CompletesStepMergesContextAndDispatchesNext()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        Dictionary<string, object?> output = new() { ["result"] = "done" };

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepCompletedMessage(execution.Id, step.Id, OrgId, output),
            CancellationToken.None);

        step.Status.Should().Be(StepExecutionStatus.Completed);
        execution.Context.Should().ContainKey("result");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteNextStepMessage>(m => m.ExecutionId == execution.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsWithoutAction()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        // Advance step to terminal
        execution.CompleteStep(step.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepCompletedMessage(execution.Id, step.Id, OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new StepCompletedMessage(Guid.NewGuid(), Guid.NewGuid(), OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepNotFound_ReturnsWithoutAction()
    {
        WorkflowExecution execution = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepCompletedMessage(execution.Id, Guid.NewGuid(), OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyException_ExitsGracefullyWithoutDispatch()
    {
        // Simulates the losing Wolverine worker in a concurrent-duplicate delivery scenario.
        // The winning worker already committed — this one must exit without dispatching.
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        await CreateHandler().HandleAsync(
            new StepCompletedMessage(execution.Id, step.Id, OrgId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
    }
}
