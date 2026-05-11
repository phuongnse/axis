using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.CreateDataClass;

/// <summary>US-037: Validates uniqueness then creates the data class.</summary>
public sealed class CreateDataClassHandler(
    IDataClassRepository dataClassRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateDataClassCommand, Guid>
{
    public async Task<Guid> Handle(CreateDataClassCommand command, CancellationToken cancellationToken)
    {
        if (await dataClassRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            throw new ValidationException($"A data class named '{command.Name}' already exists.");

        DataClass dataClass = DataClass.Create(command.Name, command.Description, command.OrganizationId, command.CreatedBy);

        await dataClassRepo.AddAsync(dataClass, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return dataClass.Id;
    }
}
