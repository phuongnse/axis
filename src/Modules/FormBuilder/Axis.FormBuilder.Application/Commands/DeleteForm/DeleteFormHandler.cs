using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.FormBuilder.Application.Commands.DeleteForm;

/// <summary>US-078: Guards against referenced forms; soft-deletes if safe.</summary>
public sealed class DeleteFormHandler(
    IFormRepository formRepo,
    IUnitOfWork uow)
    : ICommandHandler<DeleteFormCommand>
{
    public async Task Handle(DeleteFormCommand command, CancellationToken cancellationToken)
    {
        var form = await formRepo.GetByIdAsync(command.FormId, command.OrganizationId, cancellationToken);
        if (form is null)
            throw new ValidationException("Form not found.");

        // US-078: cannot delete if referenced by non-Archived workflow steps
        if (await formRepo.IsReferencedByWorkflowAsync(command.FormId, cancellationToken))
            throw new ValidationException(
                "This form is referenced by one or more workflow steps. Remove those references before deleting.");

        form.Delete();
        await uow.SaveChangesAsync(cancellationToken);
    }
}
