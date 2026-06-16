using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.UnarchiveWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class UnarchiveWorkflowHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UnarchiveWorkflowHandler _handler;

    public UnarchiveWorkflowHandlerTests() => _handler = new UnarchiveWorkflowHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenWorkflowIsArchived_UnarchivesAndSaves()
    {
        WorkflowDefinition wf = CreateArchivedWorkflow();
        _repo.GetByIdAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new UnarchiveWorkflowCommand(wf.Id, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Status.Should().Be(WorkflowStatus.Active);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TenantId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(new UnarchiveWorkflowCommand(Guid.NewGuid(), TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherTenant_ReturnsNotFound()
    {
        WorkflowDefinition wf = CreateArchivedWorkflow();

        Guid otherTenantId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherTenantId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result result = await _handler.Handle(new UnarchiveWorkflowCommand(wf.Id, otherTenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherTenantId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowIsNotArchived_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Draft", null, TenantId, "user");
        _repo.GetByIdAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(new UnarchiveWorkflowCommand(wf.Id, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }

    private static WorkflowDefinition CreateArchivedWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, TenantId, "user");
        wf.AddTrigger(TriggerType.Manual, null);
        WorkflowStep step = wf.AddStep("Review", StepType.Form, null);
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(start.Id, step.Id, null);
        wf.AddTransition(step.Id, end.Id, null);
        wf.Publish();
        wf.Archive();
        return wf;
    }
}
