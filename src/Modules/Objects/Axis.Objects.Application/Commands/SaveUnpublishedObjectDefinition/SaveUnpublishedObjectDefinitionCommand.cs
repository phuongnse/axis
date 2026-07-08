using Axis.Shared.Application.CQRS;

namespace Axis.Objects.Application.Commands.SaveUnpublishedObjectDefinition;

public sealed record SaveUnpublishedObjectDefinitionCommand(
    Guid ObjectDefinitionId,
    int ExpectedRevision,
    string Name,
    IReadOnlyList<ObjectFieldDefinitionInput> Fields)
    : ICommand<ObjectDefinitionDetailDto>;
