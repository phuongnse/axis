using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.AddFieldToForm;

public sealed class AddFieldToFormHandler(
    IFormRepository formRepo,
    IFormModelReferenceSync formModelReferenceSync,
    IUnitOfWork uow)
    : ICommandHandler<AddFieldToFormCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddFieldToFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.workspaceId, cancellationToken);
        if (form is null)
            return Result.Failure<Guid>(ErrorCodes.NotFound, "Form not found.");

        FormField field;
        try
        {
            field = form.AddField(command.Key, command.Label, command.Type, command.Required, command.Config);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure<Guid>(ErrorCodes.BusinessRule, ex.Message);
        }

        await formModelReferenceSync.SyncRelationPickerReferencesAsync(form, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return field.Id;
    }
}
