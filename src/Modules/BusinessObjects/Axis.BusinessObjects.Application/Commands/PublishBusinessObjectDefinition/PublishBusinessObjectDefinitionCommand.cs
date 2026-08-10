using Axis.Shared.Application.CQRS;

namespace Axis.BusinessObjects.Application.Commands.PublishBusinessObjectDefinition;

public sealed record PublishBusinessObjectDefinitionCommand(
    Guid BusinessObjectDefinitionId,
    int ExpectedRevision,
    string? CorrelationId = null)
    : ICommand<BusinessObjectDefinitionDetailDto>;
