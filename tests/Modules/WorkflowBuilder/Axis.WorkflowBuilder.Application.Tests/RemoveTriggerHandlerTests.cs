using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.RemoveTrigger;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class RemoveTriggerHandlerTests
{
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly RemoveTriggerHandler _handler;

    private readonly IWorkflowReferenceSync _referenceSync = Substitute.For<IWorkflowReferenceSync>();

    public RemoveTriggerHandlerTests()
    {
        _referenceSync
            .SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowReferenceSyncResult(HasBrokenReferences: false));
        _handler = new RemoveTriggerHandler(_repo, _referenceSync, _uow);
    }

    [Fact]
    public async Task Handle_WhenTriggerExists_RemovesAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TeamAccountId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        _repo.GetByIdAsync(wf.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new RemoveTriggerCommand(wf.Id, TeamAccountId, TriggerType.Manual), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Triggers.Should().BeEmpty();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new RemoveTriggerCommand(Guid.NewGuid(), TeamAccountId, TriggerType.Manual), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TeamAccountId, "user");
        wf.AddTrigger(TriggerType.Manual, null);

        Guid otherTeamAccountId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new RemoveTriggerCommand(wf.Id, otherTeamAccountId, TriggerType.Manual), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
