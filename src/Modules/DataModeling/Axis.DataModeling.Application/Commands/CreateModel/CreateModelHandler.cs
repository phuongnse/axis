using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using FluentValidation;

namespace Axis.DataModeling.Application.Commands.CreateModel;

/// <summary>US-030: Validates name uniqueness, creates model with system fields.</summary>
public sealed class CreateModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateModelCommand, Guid>
{
    public async Task<Guid> Handle(CreateModelCommand command, CancellationToken cancellationToken)
    {
        if (await modelRepo.NameExistsAsync(command.Name, command.OrganizationId, null, cancellationToken))
            throw new ValidationException($"A model named '{command.Name}' already exists.");

        var model = DataModel.Create(command.Name, command.Description, command.Icon, command.Color, command.OrganizationId);

        await modelRepo.AddAsync(model, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return model.Id;
    }
}
