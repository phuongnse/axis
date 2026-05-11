using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.FormBuilder.Application.Commands.CreateForm;

/// <summary>US-075: Validates name uniqueness, creates the form.</summary>
public sealed class CreateFormHandler(
    IFormRepository formRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateFormCommand, Guid>
{
    public async Task<Guid> Handle(CreateFormCommand command, CancellationToken cancellationToken)
    {
        if (await formRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            throw new ValidationException($"A form named '{command.Name}' already exists.");

        FormDefinition form = FormDefinition.Create(command.Name, command.Description, command.OrganizationId, command.CreatedBy);

        await formRepo.AddAsync(form, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return form.Id;
    }
}
