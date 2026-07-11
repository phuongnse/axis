using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.CreateBusinessObjectDefinition;

public sealed record CreateBusinessObjectDefinitionCommand(string Name)
    : ICommand<BusinessObjectDefinitionDetailDto>;
