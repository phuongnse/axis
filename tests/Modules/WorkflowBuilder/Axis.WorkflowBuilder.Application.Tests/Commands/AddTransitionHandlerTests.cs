using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.AddTransition;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class AddTransitionHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddTransitionHandler _handler;

    public AddTransitionHandlerTests() => _handler = new AddTransitionHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenStepsExistAndNoCycle_AddsTransitionAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, WorkspaceId, start.Id, end.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Transitions.Should().ContainSingle(t => t.FromStepId == start.Id && t.ToStepId == end.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new AddTransitionCommand(Guid.NewGuid(), WorkspaceId, Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);

        Guid otherWorkspaceId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, otherWorkspaceId, start.Id, end.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTransitionCreatesCycle_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep s1 = wf.AddStep("Step 1", StepType.Form, null);
        WorkflowStep s2 = wf.AddStep("Step 2", StepType.Form, null);
        wf.AddTransition(s1.Id, s2.Id, null);
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, WorkspaceId, s2.Id, s1.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
