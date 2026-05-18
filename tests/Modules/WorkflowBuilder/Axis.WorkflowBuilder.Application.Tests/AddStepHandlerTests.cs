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
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddStepHandler _handler;

    public AddStepHandlerTests() => _handler = new AddStepHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenWorkflowExists_AddsStepAndReturnsId()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, OrgId, "Send Email", StepType.Notification, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Steps.Should().Contain(s => s.Id == result.Value && s.Name == "Send Email");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(Guid.NewGuid(), OrgId, "Step", StepType.Form, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Guid otherOrgId = Guid.NewGuid();
        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, otherOrgId, "Step", StepType.Form, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Theory]
    [InlineData(StepType.Start)]
    [InlineData(StepType.End)]
    public async Task Handle_WhenAddingReservedStepType_ReturnsBusinessRuleError(StepType type)
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result<Guid> result = await _handler.Handle(
            new AddStepCommand(wf.Id, OrgId, "Node", type, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
