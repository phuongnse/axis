using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;
using Axis.WorkflowBuilder.Domain.Entities;

namespace Axis.WorkflowBuilder.Application.Commands.AddStep;

public sealed class AddStepHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<AddStepCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddStepCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);

        if (workflow is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Workflow not found.");

        WorkflowStep step;
        try
        {
            step = workflow.AddStep(command.Name, command.StepType, command.Config);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return step.Id;
    }
}
