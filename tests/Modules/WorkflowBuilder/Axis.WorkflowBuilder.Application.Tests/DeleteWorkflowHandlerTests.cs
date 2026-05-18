using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.DeleteWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class DeleteWorkflowHandlerTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly DeleteWorkflowHandler _handler;

    public DeleteWorkflowHandlerTests() => _handler = new DeleteWorkflowHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenWorkflowIsDraft_DeletesAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Draft Flow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new DeleteWorkflowCommand(wf.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.DeletedAt.Should().NotBeNull();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(new DeleteWorkflowCommand(Guid.NewGuid(), OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowIsActive_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = CreatePublishableWorkflow();
        wf.Publish();
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new DeleteWorkflowCommand(wf.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static WorkflowDefinition CreatePublishableWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("Review", StepType.Form, null);
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(start.Id, step.Id, null);
        wf.AddTransition(step.Id, end.Id, null);
        return wf;
    }
}
