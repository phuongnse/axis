using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.ReorderFormFields;

public sealed class ReorderFormFieldsHandler(IFormRepository formRepo, IUnitOfWork uow)
    : ICommandHandler<ReorderFormFieldsCommand>
{
    public async Task<Result> Handle(ReorderFormFieldsCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.OrganizationId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        try
        {
            form.ReorderFields(command.OrderedFieldIds);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ErrorCodes.BusinessRule, ex.Message);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
