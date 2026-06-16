using Axis.Shared.Application;
using Axis.WorkflowEngine.Application.Handlers;
using Axis.WorkflowEngine.Application.Messages;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Aggregates;
using Axis.WorkflowEngine.Domain.Enums;
using Axis.WorkflowEngine.Domain.ReadModels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowEngine.Application.Tests.Handlers;

public class ExecuteNextStepHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IWorkflowDefinitionReader _workflowReader = Substitute.For<IWorkflowDefinitionReader>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<ExecuteNextStepHandler> _logger = Substitute.For<ILogger<ExecuteNextStepHandler>>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();

    private ExecuteNextStepHandler CreateHandler() =>
        new(_execRepo, _workflowReader, _uow, _dispatcher, _logger);

    private static WorkflowExecution CreatePendingExecution()
        => WorkflowExecution.Create(WorkflowId, OrgId, TriggerType.Manual, null, new Dictionary<string, object?>());

    private static WorkflowSnapshot MakeSnapshot(Guid stepDefId, StepType stepType)
        => WorkflowSnapshot.Create(WorkflowId, OrgId,
            new List<StepDefinitionSnapshot>
            {
                new() { Id = stepDefId, Name = "Step", StepType = stepType, DisplayOrder = 0, Config = null }
            },
            new List<TransitionSnapshot>());

    // ─── Happy paths ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenNextStepIsForm_DispatchesExecuteFormStepMessage()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid formDefId = Guid.NewGuid();
        execution.AddStep(formDefId, "Form", StepType.Form, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).Returns(MakeSnapshot(formDefId, StepType.Form));

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteFormStepMessage>(m =>
                m.ExecutionId == execution.Id && m.OrganizationId == OrgId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNextStepIsHttp_DispatchesExecuteHttpStepMessage()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid httpDefId = Guid.NewGuid();
        execution.AddStep(httpDefId, "Http", StepType.HttpRequest, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).Returns(MakeSnapshot(httpDefId, StepType.HttpRequest));

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteHttpStepMessage>(m => m.ExecutionId == execution.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNextStepIsStart_AutoCompletesAndDispatchesNextStep()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid startDefId = Guid.NewGuid();
        execution.AddStep(startDefId, "Start", StepType.Start, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        execution.Steps[0].Status.Should().Be(StepExecutionStatus.Completed);
        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteNextStepMessage>(m => m.ExecutionId == execution.Id),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNextStepIsEnd_AutoCompletesAndCompletesExecution()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid endDefId = Guid.NewGuid();
        execution.AddStep(endDefId, "End", StepType.End, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        execution.Steps[0].Status.Should().Be(StepExecutionStatus.Completed);
        execution.Status.Should().Be(ExecutionStatus.Completed);
        // No further ExecuteNextStepMessage should be dispatched for End step
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAllStepsDone_CompletesExecution()
    {
        WorkflowExecution execution = CreatePendingExecution();
        Guid formDefId = Guid.NewGuid();
        ExecutionStep step = execution.AddStep(formDefId, "Form", StepType.Form, 0);

        // Manually advance execution to Running + step to Completed
        execution.Start();
        execution.StartStep(step.Id, execution.Context);
        execution.CompleteStep(step.Id, new Dictionary<string, object?>());

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        execution.Status.Should().Be(ExecutionStatus.Completed);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
    }

    // ─── Idempotency ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ExecutionStatus.Completed)]
    [InlineData(ExecutionStatus.Failed)]
    [InlineData(ExecutionStatus.Cancelled)]
    public async Task HandleAsync_WhenExecutionIsTerminal_DoesNothing(ExecutionStatus terminalStatus)
    {
        WorkflowExecution execution = CreatePendingExecution();
        execution.Start();

        if (terminalStatus == ExecutionStatus.Completed) execution.Complete();
        else if (terminalStatus == ExecutionStatus.Failed) execution.Fail("err");
        else execution.Cancel();

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    // ─── Not found ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenExecutionNotFound_ReturnsWithoutAction()
    {
        _execRepo.GetByIdWithStepsAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).ReturnsNull();

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(Guid.NewGuid(), OrgId), CancellationToken.None);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    // ─── Optimistic concurrency ──────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyExceptionOnStartStep_ExitsGracefullyWithoutDispatch()
    {
        // Concurrent delivery on the Start/End inline-complete path.
        WorkflowExecution execution = CreatePendingExecution();
        Guid startDefId = Guid.NewGuid();
        execution.AddStep(startDefId, "Start", StepType.Start, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        // The losing worker must not dispatch a follow-up message.
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteNextStepMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSaveThrowsConcurrencyExceptionOnRegularStep_ExitsGracefullyWithoutDispatch()
    {
        // Concurrent delivery on the step-start path for typed handlers (Http, Form, etc.).
        WorkflowExecution execution = CreatePendingExecution();
        Guid formDefId = Guid.NewGuid();
        execution.AddStep(formDefId, "Form", StepType.Form, 0);

        _execRepo.GetByIdWithStepsAsync(execution.Id, OrgId).Returns(execution);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).Returns(MakeSnapshot(formDefId, StepType.Form));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new ConcurrencyException()));

        await CreateHandler().HandleAsync(
            new ExecuteNextStepMessage(execution.Id, OrgId), CancellationToken.None);

        // The losing worker must not dispatch the typed handler message.
        await _dispatcher.DidNotReceive().PublishAsync(
            Arg.Any<ExecuteFormStepMessage>(), Arg.Any<CancellationToken>());
    }
}
