using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.WorkflowBuilder.Application.Tests.Commands;

public class PublishWorkflowHandlerTests
{
    private readonly IWorkflowRepository _workflowRepo = Substitute.For<IWorkflowRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    private PublishWorkflowHandler CreateHandler() => new(_workflowRepo, _uow);

    private static WorkflowDefinition MakePublishableWorkflow()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        wf.AddTrigger(TriggerType.Manual, null);
        Domain.Entities.WorkflowStep formStep = wf.AddStep("Review", StepType.Form, null);
        Domain.Entities.WorkflowStep startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        Domain.Entities.WorkflowStep endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);
        return wf;
    }

    [Fact]
    public async Task PublishWorkflow_WhenWorkflowIsValid_PublishesWorkflow()
    {
        WorkflowDefinition wf = MakePublishableWorkflow();
        _workflowRepo.GetByIdAsync(wf.Id, OrgId).Returns(wf);

        Result result = await CreateHandler().Handle(new PublishWorkflowCommand(wf.Id, OrgId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        wf.Status.Should().Be(WorkflowStatus.Active);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishWorkflow_WhenWorkflowNotFound_ReturnsNotFound()
    {
        _workflowRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        Result result = await CreateHandler().Handle(
            new PublishWorkflowCommand(Guid.NewGuid(), OrgId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task PublishWorkflow_WhenWorkflowBelongsToAnotherOrg_ReturnsNotFound()
    {
        WorkflowDefinition wf = MakePublishableWorkflow();

        Guid otherOrgId = Guid.NewGuid();
        _workflowRepo.GetByIdAsync(wf.Id, otherOrgId).Returns((WorkflowDefinition?)null);
        Result result = await CreateHandler().Handle(
            new PublishWorkflowCommand(wf.Id, otherOrgId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await _workflowRepo.Received(1).GetByIdAsync(wf.Id, otherOrgId);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishWorkflow_WhenNoTriggersConfigured_ReturnsBusinessRuleFailure()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        // No trigger added — should fail domain validation
        wf.AddStep("Review", StepType.Form, null);
        _workflowRepo.GetByIdAsync(wf.Id, OrgId).Returns(wf);

        Result result = await CreateHandler().Handle(
            new PublishWorkflowCommand(wf.Id, OrgId),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("trigger");
    }
}
