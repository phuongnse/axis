using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.RemoveStep;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class RemoveStepHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly RemoveStepHandler _handler;

    private readonly IWorkflowReferenceSync _referenceSync = Substitute.For<IWorkflowReferenceSync>();

    public RemoveStepHandlerTests()
    {
        _referenceSync
            .SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowReferenceSyncResult(HasBrokenReferences: false));
        _handler = new RemoveStepHandler(_repo, _referenceSync, _uow);
    }

    [Fact]
    public async Task Handle_WhenStepExists_RemovesStepAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep step = wf.AddStep("Review", StepType.Form, null);
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new RemoveStepCommand(wf.Id, WorkspaceId, step.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Steps.Should().NotContain(s => s.Id == step.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new RemoveStepCommand(Guid.NewGuid(), WorkspaceId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep step = wf.AddStep("Review", StepType.Form, null);

        Guid otherWorkspaceId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(
            new RemoveStepCommand(wf.Id, otherWorkspaceId, step.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRemovingStartOrEnd_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, WorkspaceId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new RemoveStepCommand(wf.Id, WorkspaceId, start.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
