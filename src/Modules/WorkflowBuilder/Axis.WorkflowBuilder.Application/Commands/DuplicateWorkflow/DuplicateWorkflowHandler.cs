using Axis.Shared.Application.CQRS;
using Axis.Shared.Application.PlanLimits;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;

public sealed class DuplicateWorkflowHandler(
    IPlanLimitService planLimitService,
    IWorkflowRepository workflowRepo,
    IUnitOfWork uow)
    : ICommandHandler<DuplicateWorkflowCommand, Guid>
{
    public async Task<Result<Guid>> Handle(DuplicateWorkflowCommand command, CancellationToken cancellationToken)
    {
        Result planCheck = await planLimitService.EnsureWithinLimitAsync(
            command.TeamAccountId,
            PlanLimitResourceType.Workflows,
            increment: 1,
            cancellationToken);
        if (planCheck.IsFailure)
        {
            if (planCheck.PlanLimitDetails is PlanLimitFailureDetails details)
                return Result<Guid>.PlanLimitFailure(details);
            return Result.Failure<Guid>(planCheck.ErrorCode!, planCheck.Error);
        }

        WorkflowDefinition? original = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.TeamAccountId, cancellationToken);

        if (original is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Workflow not found.");

        WorkflowDefinition copy = original.Duplicate();

        string resolvedName = await ResolveUniqueCopyNameAsync(
            copy.Name, command.TeamAccountId, cancellationToken);

        if (resolvedName != copy.Name)
            copy.Update(resolvedName, copy.Description);

        await workflowRepo.AddAsync(copy, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await planLimitService.RecordUsageDeltaAsync(
            command.TeamAccountId,
            PlanLimitResourceType.Workflows,
            delta: 1,
            cancellationToken);
        return copy.Id;
    }

    private async Task<string> ResolveUniqueCopyNameAsync(
        string baseName, Guid teamAccountId, CancellationToken cancellationToken)
    {
        if (!await workflowRepo.NameExistsAsync(baseName, teamAccountId, null, cancellationToken))
            return baseName;

        for (int suffix = 2; suffix <= 50; suffix++)
        {
            string candidate = $"{baseName} ({suffix})";
            if (!await workflowRepo.NameExistsAsync(candidate, teamAccountId, null, cancellationToken))
                return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():N})";
    }
}
