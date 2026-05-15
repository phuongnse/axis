using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.UpdateForm;

public sealed class UpdateFormHandler(IFormRepository formRepo, IUnitOfWork uow)
    : ICommandHandler<UpdateFormCommand>
{
    public async Task<Result> Handle(UpdateFormCommand command, CancellationToken cancellationToken)
    {
        FormDefinition? form = await formRepo.GetByIdAsync(command.FormId, command.OrganizationId, cancellationToken);
        if (form is null)
            return Result.Failure(ErrorCodes.NotFound, "Form not found.");

        if (await formRepo.NameExistsAsync(command.Name, command.OrganizationId, command.FormId, cancellationToken))
            return Result.Failure(ErrorCodes.Conflict, $"A form named '{command.Name}' already exists.");

        form.Update(command.Name, command.Description);
        await uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
