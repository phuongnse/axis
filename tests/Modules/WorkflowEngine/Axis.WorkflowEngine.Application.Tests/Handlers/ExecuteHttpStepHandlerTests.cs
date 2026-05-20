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

public class ExecuteHttpStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IHttpStepExecutor _executor = Substitute.For<IHttpStepExecutor>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<ExecuteHttpStepHandler> _logger = Substitute.For<ILogger<ExecuteHttpStepHandler>>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private ExecuteHttpStepHandler CreateHandler() => new(_execRepo, _executor, _dispatcher, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakePendingStep()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Http", StepType.HttpRequest, 0);
        exec.Start();
        return (exec, step);
    }

    private static ExecuteHttpStepMessage MakeMessage(WorkflowExecution exec, ExecutionStep step)
        => new(exec.Id, step.Id, OrgId, null, exec.Context);

    [Fact]
    public async Task HandleAsync_WhenExecutorSucceeds_DispatchesStepCompletedMessage()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        Dictionary<string, object?> output = new() { ["status_code"] = 200 };

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _executor.ExecuteAsync(Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(output);

        await CreateHandler().HandleAsync(MakeMessage(execution, step), CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m =>
                m.ExecutionId == execution.Id && m.StepId == step.Id),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<StepFailedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutorThrows_DispatchesStepFailedMessage()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _executor.ExecuteAsync(
                Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        await CreateHandler().HandleAsync(MakeMessage(execution, step), CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepFailedMessage>(m =>
                m.ExecutionId == execution.Id &&
                m.ErrorDetails.Contains("Connection refused")),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<StepCompletedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsExecution()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        // Advance step to terminal: need Running first to fail/complete
        execution.StartStep(step.Id, execution.Context);
        execution.CompleteStep(step.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(MakeMessage(execution, step), CancellationToken.None);

        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsRunning_ExecutesNormally()
    {
        // Step is Running because ExecuteNextStepHandler starts it before dispatching.
        // The Running state should NOT skip execution — it is the expected state on first delivery.
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        execution.StartStep(step.Id, execution.Context);

        Dictionary<string, object?> output = new() { ["status_code"] = 200 };
        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _executor.ExecuteAsync(Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(output);

        await CreateHandler().HandleAsync(MakeMessage(execution, step), CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m => m.StepId == step.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteHttpStepMessage(Guid.NewGuid(), Guid.NewGuid(), OrgId, null, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
