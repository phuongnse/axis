using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.AddTrigger;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class AddTriggerHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddTriggerHandler _handler;

    private readonly IWorkflowReferenceSync _referenceSync = Substitute.For<IWorkflowReferenceSync>();

    public AddTriggerHandlerTests()
    {
        _referenceSync
            .SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowReferenceSyncResult(HasBrokenReferences: false));
        _handler = new AddTriggerHandler(_repo, _referenceSync, _uow);
    }

    [Fact]
    public async Task Handle_WhenTriggerTypeIsNew_AddsTriggerAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTriggerCommand(wf.Id, OrgId, TriggerType.Manual, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Triggers.Should().ContainSingle(t => t.Type == TriggerType.Manual);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new AddTriggerCommand(Guid.NewGuid(), OrgId, TriggerType.Manual, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");

        Guid otherOrgId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new AddTriggerCommand(wf.Id, otherOrgId, TriggerType.Manual, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherOrgId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTriggerTypeAlreadyExists_ReturnsConflict()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTriggerCommand(wf.Id, OrgId, TriggerType.Manual, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
    }
}
