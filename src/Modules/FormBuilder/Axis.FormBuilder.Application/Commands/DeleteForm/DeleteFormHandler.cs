using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.DeleteForm;

/// <summary>US-078: Guards against referenced forms; soft-deletes if safe.</summary>
public sealed class DeleteFormHandler(
    IFormRepository formRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteFormCommand>
{
    public async Task<Result> Handle(DeleteFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.OrganizationId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        // US-078: cannot delete if referenced by non-Archived workflow steps
        if (await formRepo.IsReferencedByWorkflowAsync(command.FormId, cancellationToken))
            return Result.Failure(ErrorCodes.BusinessRule,
                "This form is referenced by one or more active workflow steps. Remove those references before deleting.");

        form.Delete();
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
