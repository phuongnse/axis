using Axis.WorkflowBuilder.Application.Commands.PublishWorkflow;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Enums;
using FluentAssertions;
using FluentValidation;
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
        var formStep = wf.AddStep("Review", StepType.Form, null);
        var startStep = wf.Steps.Single(s => s.Type == StepType.Start);
        var endStep = wf.Steps.Single(s => s.Type == StepType.End);
        wf.AddTransition(startStep.Id, formStep.Id, null);
        wf.AddTransition(formStep.Id, endStep.Id, null);
        return wf;
    }

    [Fact]
    public async Task Happy_path_publishes_workflow()
    {
        var wf = MakePublishableWorkflow();
        _workflowRepo.GetByIdAsync(wf.Id, OrgId).Returns(wf);

        await CreateHandler().Handle(new PublishWorkflowCommand(wf.Id, OrgId), CancellationToken.None);

        wf.Status.Should().Be(WorkflowStatus.Active);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Workflow_not_found_throws_validation_exception()
    {
        _workflowRepo.GetByIdAsync(Arg.Any<Guid>(), OrgId).ReturnsNull();

        var act = async () => await CreateHandler().Handle(
            new PublishWorkflowCommand(Guid.NewGuid(), OrgId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task Workflow_without_triggers_throws_on_publish()
    {
        WorkflowDefinition wf = WorkflowDefinition.Create("Invoice Approval", null, OrgId, UserId);
        // No trigger added — should fail domain validation
        var formStep = wf.AddStep("Review", StepType.Form, null);
        _workflowRepo.GetByIdAsync(wf.Id, OrgId).Returns(wf);

        var act = async () => await CreateHandler().Handle(
            new PublishWorkflowCommand(wf.Id, OrgId),
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*trigger*");
    }
}
