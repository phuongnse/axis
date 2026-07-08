using Axis.Shared.Application.CQRS;

namespace Axis.Objects.Application.Commands.CreateObjectDefinition;

public sealed record CreateObjectDefinitionCommand(string Name)
    : ICommand<ObjectDefinitionDetailDto>;
