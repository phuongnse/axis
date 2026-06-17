using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.CreateWorkflow;

/// <summary>Validates name uniqueness, creates workflow with Start/End nodes.</summary>
public sealed class CreateWorkflowHandler(
    IPlanLimitService planLimitService,
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateWorkflowCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateWorkflowCommand command, CancellationToken cancellationToken)
    {
        Result planCheck = await planLimitService.EnsureWithinLimitAsync(
            command.workspaceId,
            PlanLimitResourceType.Workflows,
            increment: 1,
            cancellationToken);
        if (planCheck.IsFailure)
        {
            if (planCheck.PlanLimitDetails is PlanLimitFailureDetails details)
                return Result<Guid>.PlanLimitFailure(details);
            return Result.Failure<Guid>(planCheck.ErrorCode!, planCheck.Error);
        }

        if (await workflowRepo.NameExistsAsync(command.Name, command.workspaceId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A workflow named '{command.Name}' already exists.");

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            command.Name, command.Description, command.workspaceId, command.CreatedBy);

        await workflowRepo.AddAsync(workflow, cancellationToken);

        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A workflow named '{command.Name}' already exists.");
        }

        await planLimitService.RecordUsageDeltaAsync(
            command.workspaceId,
            PlanLimitResourceType.Workflows,
            delta: 1,
            cancellationToken);

        return workflow.Id;
    }
}
