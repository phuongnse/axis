using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.DeleteWorkflow;

public sealed class DeleteWorkflowHandler(
    IPlanLimitService planLimitService,
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteWorkflowCommand>
{
    public async Task<Result> Handle(DeleteWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? workflow = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.tenantId, cancellationToken);

        if (workflow is null)
            return Result.Failure(ErrorCodes.NotFound, "Workflow not found.");

        try
        {
            workflow.Delete();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RecordUsageDeltaAsync(
            command.tenantId,
            PlanLimitResourceType.Workflows,
            delta: -1,
            cancellationToken);
        return Result.Success();
    }
}
