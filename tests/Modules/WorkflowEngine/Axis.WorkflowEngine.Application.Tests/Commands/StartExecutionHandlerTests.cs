using Axis.WorkflowEngine.Application.Commands.StartExecution;
using Axis.WorkflowEngine.Application.Repositories;
using Axis.WorkflowEngine.Application.Services;
using Axis.WorkflowEngine.Domain.Enums;
using FluentAssertions;
using FluentValidation;
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

        var result = await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.Should().NotBeEmpty();
        await _execRepo.Received(1).AddAsync(
            Arg.Is<Domain.Aggregates.WorkflowExecution>(e =>
                e.WorkflowDefinitionId == WorkflowId &&
                e.Status == ExecutionStatus.Pending),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Throws_when_workflow_is_not_active()
    {
        _workflowReader.IsActiveAsync(WorkflowId, OrgId).Returns(false);

        var act = async () => await CreateHandler().Handle(
            new StartExecutionCommand(WorkflowId, OrgId, TriggerType.Manual, UserId, new Dictionary<string, object?>()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*cannot be triggered*");
    }
}
