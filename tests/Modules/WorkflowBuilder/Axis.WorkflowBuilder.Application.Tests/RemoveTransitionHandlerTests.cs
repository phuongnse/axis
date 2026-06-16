using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.RemoveTransition;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class RemoveTransitionHandlerTests
{
    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly RemoveTransitionHandler _handler;

    public RemoveTransitionHandlerTests() => _handler = new RemoveTransitionHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenTransitionExists_RemovesAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TeamAccountId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(start.Id, end.Id, null);
        _repo.GetByIdAsync(wf.Id, TeamAccountId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new RemoveTransitionCommand(wf.Id, TeamAccountId, start.Id, end.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Transitions.Should().BeEmpty();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TeamAccountId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new RemoveTransitionCommand(Guid.NewGuid(), TeamAccountId, Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherTeamAccount_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TeamAccountId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(start.Id, end.Id, null);

        Guid otherTeamAccountId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new RemoveTransitionCommand(wf.Id, otherTeamAccountId, start.Id, end.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherTeamAccountId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
