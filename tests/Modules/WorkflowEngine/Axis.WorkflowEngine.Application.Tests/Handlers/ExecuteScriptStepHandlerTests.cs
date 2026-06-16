using Axis.WorkflowEngine.Application.Handlers;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Handlers;

public class ExecuteScriptStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IScriptExecutor _executor = Substitute.For<IScriptExecutor>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<ExecuteScriptStepHandler> _logger = Substitute.For<ILogger<ExecuteScriptStepHandler>>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private ExecuteScriptStepHandler CreateHandler() => new(_execRepo, _executor, _dispatcher, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakePendingStep()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, WorkspaceId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Script", StepType.Script, 0);
        exec.Start();
        return (exec, step);
    }

    [Fact]
    public async Task HandleAsync_WhenExecutorSucceeds_DispatchesStepCompletedMessage()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        Dictionary<string, object?> output = new() { ["computed"] = 42 };

        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);
        _executor.ExecuteAsync(Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(output);

        await CreateHandler().HandleAsync(
            new ExecuteScriptStepMessage(execution.Id, step.Id, WorkspaceId, null, execution.Context),
            CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m => m.ExecutionId == execution.Id && m.StepId == step.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutorThrows_DispatchesStepFailedMessage()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();

        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);
        _executor.ExecuteAsync(
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Script error"));

        await CreateHandler().HandleAsync(
            new ExecuteScriptStepMessage(execution.Id, step.Id, WorkspaceId, null, execution.Context),
            CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepFailedMessage>(m => m.ErrorDetails == nameof(InvalidOperationException)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsExecution()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        execution.StartStep(step.Id, execution.Context);
        execution.CompleteStep(step.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, WorkspaceId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteScriptStepMessage(execution.Id, step.Id, WorkspaceId, null, execution.Context),
            CancellationToken.None);

        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteScriptStepMessage(Guid.NewGuid(), Guid.NewGuid(), WorkspaceId, null, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
