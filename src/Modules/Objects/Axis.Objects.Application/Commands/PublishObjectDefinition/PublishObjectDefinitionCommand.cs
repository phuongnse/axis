using Axis.Shared.Application.CQRS;

namespace Axis.Objects.Application.Commands.PublishObjectDefinition;

public sealed record PublishObjectDefinitionCommand(
    Guid ObjectDefinitionId,
    int ExpectedRevision)
    : ICommand<ObjectDefinitionDetailDto>;
