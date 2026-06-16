using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.UpdateForm;

public sealed class UpdateFormHandler(IFormRepository formRepo, IUnitOfWork uow)
    : ICommandHandler<UpdateFormCommand>
{
    public async Task<Result> Handle(UpdateFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.tenantId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        if (await formRepo.NameExistsAsync(command.Name, command.tenantId, command.FormId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, $"A form named '{command.Name}' already exists.");

        form.Update(command.Name, command.Description);

        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure(ErrorCodes.Conflict, $"A form named '{command.Name}' already exists.");
        }

        return Result.Success();
    }
}
