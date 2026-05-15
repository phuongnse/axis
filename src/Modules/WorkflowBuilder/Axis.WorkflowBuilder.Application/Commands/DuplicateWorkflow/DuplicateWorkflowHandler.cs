using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Repositories;
using Axis.WorkflowBuilder.Application.Services;
using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Commands.DuplicateWorkflow;

public sealed class DuplicateWorkflowHandler(IWorkflowRepository workflowRepo, IUnitOfWork uow)
    : ICommandHandler<DuplicateWorkflowCommand, Guid>
{
    public async Task<Result<Guid>> Handle(DuplicateWorkflowCommand command, CancellationToken cancellationToken)
    {
        WorkflowDefinition? original = await workflowRepo.GetByIdAsync(
            command.WorkflowId, command.OrganizationId, cancellationToken);

        if (original is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Workflow not found.");

        WorkflowDefinition copy = original.Duplicate();

        string resolvedName = await ResolveUniqueCopyNameAsync(
            copy.Name, command.OrganizationId, cancellationToken);

        if (resolvedName != copy.Name)
            copy.Update(resolvedName, copy.Description);

        await workflowRepo.AddAsync(copy, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return copy.Id;
    }

    private async Task<string> ResolveUniqueCopyNameAsync(
        string baseName, Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await workflowRepo.NameExistsAsync(baseName, organizationId, null, cancellationToken))
            return baseName;

        for (int suffix = 2; suffix <= 50; suffix++)
        {
            string candidate = $"{baseName} ({suffix})";
            if (!await workflowRepo.NameExistsAsync(candidate, organizationId, null, cancellationToken))
                return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():N})";
    }
}
