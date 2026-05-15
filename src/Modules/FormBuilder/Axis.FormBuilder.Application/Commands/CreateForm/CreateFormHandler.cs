using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Application.Commands.CreateForm;

/// <summary>US-075: Validates name uniqueness, creates the form.</summary>
public sealed class CreateFormHandler(
    IFormRepository formRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateFormCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateFormCommand command, CancellationToken cancellationToken)
    {
        if (await formRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A form named '{command.Name}' already exists.");

        FormDefinition form = FormDefinition.Create(
            command.Name, command.Description, command.OrganizationId, command.CreatedBy);

        await formRepo.AddAsync(form, cancellationToken);

        try
        {
            await uow.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintException)
        {
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A form named '{command.Name}' already exists.");
        }

        return form.Id;
    }
}
