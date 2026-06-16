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

public class StepFailedHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<StepFailedHandler> _logger = Substitute.For<ILogger<StepFailedHandler>>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private StepFailedHandler CreateHandler() => new(_execRepo, _uow, _logger);

    private static (WorkflowExecution Execution, ExecutionStep Step) MakeRunningExecution()
    {
        WorkflowExecution exec = WorkflowExecution.Create(WorkflowId, TeamAccountId, TriggerType.Manual, null, new Dictionary<string, object?>());
        ExecutionStep step = exec.AddStep(Guid.NewGuid(), "Http", StepType.HttpRequest, 0);
        exec.Start();
        exec.StartStep(step.Id, exec.Context);
        return (exec, step);
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsRunning_FailsStepAndExecution()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepFailedMessage(execution.Id, step.Id, TeamAccountId, "Timeout"),
            CancellationToken.None);

        step.Status.Should().Be(StepExecutionStatus.Failed);
        step.ErrorDetails.Should().Contain("Timeout");
        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("Timeout");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepIsAlreadyTerminal_SkipsWithoutAction()
    {
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        execution.FailStep(step.Id, "prior error");
        execution.Fail("prior error");

        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepFailedMessage(execution.Id, step.Id, TeamAccountId, "duplicate"),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new StepFailedMessage(Guid.NewGuid(), Guid.NewGuid(), TeamAccountId, "error"),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenStepNotFound_ReturnsWithoutAction()
    {
        WorkflowExecution execution = WorkflowExecution.Create(WorkflowId, TeamAccountId, TriggerType.Manual, null, new Dictionary<string, object?>());
        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);

        await CreateHandler().HandleAsync(
            new StepFailedMessage(execution.Id, Guid.NewGuid(), TeamAccountId, "error"),
            CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyException_ExitsGracefully()
    {
        // The losing worker must not propagate the exception — the winning instance already persisted the failure.
        (WorkflowExecution execution, ExecutionStep step) = MakeRunningExecution();
        _execRepo.GetByIdWithStepsAsync(execution.Id, TeamAccountId).Returns(execution);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        // Act — must complete without throwing
        Func<Task> act = () => CreateHandler().HandleAsync(
            new StepFailedMessage(execution.Id, step.Id, TeamAccountId, "Timeout"),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
