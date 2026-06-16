using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.RemoveFieldFromForm;

public sealed class RemoveFieldFromFormHandler(
    IFormRepository formRepo,
    IFormModelReferenceSync formModelReferenceSync,
    IUnitOfWork uow)
    : ICommandHandler<RemoveFieldFromFormCommand>
{
    public async Task<Result> Handle(RemoveFieldFromFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.TeamAccountId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        try
        {
            form.RemoveField(command.FieldId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await formModelReferenceSync.SyncRelationPickerReferencesAsync(form, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
