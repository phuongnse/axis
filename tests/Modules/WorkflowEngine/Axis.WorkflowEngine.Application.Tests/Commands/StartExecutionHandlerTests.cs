using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.StartExecution;
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

namespace Axis.WorkflowEngine.Application.Tests.Commands;

public class StartExecutionHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IWorkflowDefinitionReader _workflowReader = Substitute.For<IWorkflowDefinitionReader>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IStepDispatcher _dispatcher = Substitute.For<IStepDispatcher>();
    private readonly ILogger<StartExecutionHandler> _logger = Substitute.For<ILogger<StartExecutionHandler>>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private StartExecutionHandler CreateHandler() => new(_execRepo, _workflowReader, _uow, _dispatcher, _logger);

    private static WorkflowSnapshot MakeSnapshot() => WorkflowSnapshot.Create(
        WorkflowId, OrgId,
        new List<StepDefinitionSnapshot>
        {
            new() { Id = Guid.NewGuid(), Name = "Start", StepType = StepType.Start, DisplayOrder = 0 },
            new() { Id = Guid.NewGuid(), Name = "Form", StepType = StepType.Form, DisplayOrder = 1 }
        },
        new List<TransitionSnapshot>());

    [Fact]
    public async Task StartExecution_WhenWorkflowIsActive_InitialisesStepsAndDispatchesNextStep()
    {
        WorkflowSnapshot snapshot = MakeSnapshot();
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(true);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).Returns(snapshot);

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<WorkflowExecution>(e =>
                e.WorkflowDefinitionId == WorkflowId &&
                e.Status == ExecutionStatus.Pending &&
                e.Steps.Count == 2),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _dispatcher.Received(1).PublishAsync(
            Arg.Is<ExecuteNextStepMessage>(m => m.OrganizationId == OrgId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartExecution_WhenWorkflowIsNotActive_ReturnsBusinessRuleFailure()
    {
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cannot be triggered");
    }

    [Fact]
    public async Task StartExecution_WhenInputIsNull_DefaultsToEmptyDictionary()
    {
        WorkflowSnapshot snapshot = MakeSnapshot();
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(true);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).Returns(snapshot);

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<WorkflowExecution>(e => e.Context.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartExecution_WhenSnapshotNotFound_ReturnsBusinessRuleFailure()
    {
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(true);
        _workflowReader.GetSnapshotAsync(WorkflowId, OrgId).ReturnsNull();

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("snapshot");
    }
}
