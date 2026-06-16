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

public class ExecuteNotificationStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<ExecuteNotificationStepHandler> _logger = Substitute.For<ILogger<ExecuteNotificationStepHandler>>();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private ExecuteNotificationStepHandler CreateHandler() => new(_execRepo, _sender, _dispatcher, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakePendingStep()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, TenantId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Notify", StepType.Notification, 0);
        exec.Start();
        return (exec, step);
    }

    [Fact]
    public async Task HandleAsync_WhenDeliverySucceeds_DispatchesStepCompletedMessage()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        Dictionary<string, object?> deliveryResult = new() { ["status"] = "sent" };

        _execRepo.GetByIdWithStepsAsync(execution.Id, TenantId).Returns(execution);
        _sender.SendAsync(Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(deliveryResult);

        await CreateHandler().HandleAsync(
            new ExecuteNotificationStepMessage(execution.Id, step.Id, TenantId, null, execution.Context),
            CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m => m.ExecutionId == execution.Id && m.StepId == step.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenDeliveryFails_StillDispatchesStepCompletedMessage()
    {
        // Notification steps always complete — delivery failures are logged but not fatal
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        Dictionary<string, object?> failedResult = new() { ["status"] = "failed", ["error"] = "SMTP down" };

        _execRepo.GetByIdWithStepsAsync(execution.Id, TenantId).Returns(execution);
        _sender.SendAsync(Arg.Any<IReadOnlyDictionary<string, object?>?>(),
                Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(failedResult);

        await CreateHandler().HandleAsync(
            new ExecuteNotificationStepMessage(execution.Id, step.Id, TenantId, null, execution.Context),
            CancellationToken.None);

        // Step completes regardless of delivery outcome
        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<StepCompletedMessage>(m => m.ExecutionId == execution.Id),
            Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<StepFailedMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsExecution()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakePendingStep();
        execution.StartStep(step.Id, execution.Context);
        execution.CompleteStep(step.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, TenantId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteNotificationStepMessage(execution.Id, step.Id, TenantId, null, execution.Context),
            CancellationToken.None);

        await _sender.DidNotReceive().SendAsync(
            Arg.Any<IReadOnlyDictionary<string, object?>?>(),
            Arg.Any<IReadOnlyDictionary<string, object?>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteNotificationStepMessage(Guid.NewGuid(), Guid.NewGuid(), TenantId, null, new Dictionary<string, object?>()),
            CancellationToken.None);

        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
