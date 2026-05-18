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
    private static readonly Guid OrgId = Guid.NewGuid();
    private readonly IWorkflowRepository _repo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly AddTransitionHandler _handler;

    public AddTransitionHandlerTests() => _handler = new AddTransitionHandler(_repo, _uow);

    [Fact]
    public async Task Handle_WhenStepsExistAndNoCycle_AddsTransitionAndSaves()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, OrgId, start.Id, end.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Transitions.Should().ContainSingle(t => t.FromStepId == start.Id && t.ToStepId == end.Id);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _repo.GetByIdAsync(Arg.Any<Guid>(), OrgId, Arg.Any<CancellationToken>()).Returns((WorkflowDefinition?)null);

        Result result = await _handler.Handle(
            new AddTransitionCommand(Guid.NewGuid(), OrgId, Guid.NewGuid(), Guid.NewGuid(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        WorkflowStep start = wf.Steps.Single(s => s.Type == StepType.Start);
        WorkflowStep end = wf.Steps.Single(s => s.Type == StepType.End);
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Guid otherOrgId = Guid.NewGuid();
        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, otherOrgId, start.Id, end.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task Handle_WhenTransitionCreatesCycle_ReturnsBusinessRuleError()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("My Workflow", null, OrgId, "user");
        WorkflowStep s1 = wf.AddStep("Step 1", StepType.Form, null);
        WorkflowStep s2 = wf.AddStep("Step 2", StepType.Form, null);
        wf.AddTransition(s1.Id, s2.Id, null);
        _repo.GetByIdAsync(wf.Id, OrgId, Arg.Any<CancellationToken>()).Returns(wf);

        Result result = await _handler.Handle(
            new AddTransitionCommand(wf.Id, OrgId, s2.Id, s1.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
    }
}
