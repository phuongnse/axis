using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.AddStep;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace Axis.WorkflowBuilder.Application.Tests;

public class AddStepHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddStepHandler _handler;

    private readonly IWorkflowReferenceSync _referenceSync = Substitute.For<IWorkflowReferenceSync>();

    public AddStepHandlerTests()
    {
        _referenceSync
            .SyncAsync(Arg.Any<WorkflowDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowReferenceSyncResult(HasBrokenReferences: false));
        _handler = new AddStepHandler(_repo, _referenceSync, _uow);
    }

    [Fact]
    public async Task Handle_WhenWorkflowExists_AddsStepAndReturnsId()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TenantId, "user");
        _repo.GetByIdAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, TenantId, "Send Email", StepType.Notification, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Steps.Should().Contain(s => s.Id == result.Value && s.Name == "Send Email");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), TenantId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(Guid.NewGuid(), TenantId, "Step", StepType.Form, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherTenant_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TenantId, "user");

        Guid otherTenantId = Guid.NewGuid();
        _repo.GetByIdAsync(wf.Id, otherTenantId, Arg.Any<CancellationToken>())
            .Returns((WorkflowDefinition?)null);
        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, otherTenantId, "Step", StepType.Form, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _repo.Received(1).GetByIdAsync(wf.Id, otherTenantId, Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(StepType.Start)]
    [InlineData(StepType.End)]
    public async Task Handle_WhenAddingReservedStepType_ReturnsBusinessRuleError(StepType type)
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, TenantId, "user");
        _repo.GetByIdAsync(wf.Id, TenantId, Arg.Any<CancellationToken>()).Returns(wf);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, TenantId, "Node", type, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
