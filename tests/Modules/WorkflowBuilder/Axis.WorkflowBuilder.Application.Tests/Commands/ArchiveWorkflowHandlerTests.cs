using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.ArchiveWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class ArchiveWorkflowHandlerTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ArchiveWorkflowHandler _handler;

    public ArchiveWorkflowHandlerTests() => _handler = new ArchiveWorkflowHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenWorkflowIsActive_ArchivesAndSaves()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new ArchiveWorkflowCommand(wf.Id, WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Status.Should().Be(WorkflowStatus.Archived);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), WorkspaceId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(new ArchiveWorkflowCommand(Guid.NewGuid(), WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();

        Guid otherWorkspaceId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(new ArchiveWorkflowCommand(wf.Id, otherWorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherWorkspaceId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowIsDraft_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Draft", null, WorkspaceId, "user");
        _repo.GetByIdAsync(wf.Id, WorkspaceId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new ArchiveWorkflowCommand(wf.Id, WorkspaceId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static WorkflowDefinition CreatePublishableWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, WorkspaceId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("Review", StepType.Form, null);
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(start.Id, step.Id, null);
        wf.AddTransition(step.Id, end.Id, null);
        return wf;
    }
}
