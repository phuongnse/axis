using Axis.DataModeling.Application.Repositories;
using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Application.Commands.CreateModel;

/// <summary>Validates name uniqueness, creates model with system fields.</summary>
public sealed class CreateModelHandler(
    IDataModelRepository modelRepo,
    IUnitOfWork uow)
    : ICommandHandler<CreateModelCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateModelCommand command, CancellationToken cancellationToken)
    {
        if (await modelRepo.NameExistsAsync(command.Name, command.tenantId, null, cancellationToken))
            return Result.Failure<Guid>(ErrorCodes.Conflict, $"A model named '{command.Name}' already exists.");

        DataModel model = DataModel.Create(command.Name, command.Description, command.Icon, command.Color, command.tenantId, command.CreatedBy);

        await modelRepo.AddAsync(model, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        return model.Id;
    }
}
