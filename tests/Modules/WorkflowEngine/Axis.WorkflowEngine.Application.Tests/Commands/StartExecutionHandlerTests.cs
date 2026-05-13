using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Application.Commands.StartExecution;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowEngine.Application.Tests.Commands;

public class StartExecutionHandlerTests
{
    private readonly IExecutionRepository _execRepo = Substitute.For<IExecutionRepository>();
    private readonly IWorkflowDefinitionReader _workflowReader = Substitute.For<IWorkflowDefinitionReader>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid WorkflowId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private StartExecutionHandler CreateHandler() => new(_execRepo, _workflowReader, _uow);

    [Fact]
    public async Task Happy_path_creates_pending_execution_and_returns_id()
    {
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.WorkflowExecution>(e =>
                e.WorkflowDefinitionId == WorkflowId &&
                e.Status == ExecutionStatus.Pending),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_not_active_returns_business_rule_failure()
    {
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("cannot be triggered");
    }
}
