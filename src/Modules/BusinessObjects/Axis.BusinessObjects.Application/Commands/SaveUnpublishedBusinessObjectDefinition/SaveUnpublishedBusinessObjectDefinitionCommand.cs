using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.SaveUnpublishedBusinessObjectDefinition;

public sealed record SaveUnpublishedBusinessObjectDefinitionCommand(
    Guid BusinessObjectDefinitionId,
    int ExpectedRevision,
    string Name,
    IReadOnlyList<BusinessObjectFieldDefinitionInput> Fields)
    : ICommand<BusinessObjectDefinitionDetailDto>;
