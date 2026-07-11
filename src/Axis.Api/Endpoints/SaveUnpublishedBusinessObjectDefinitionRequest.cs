using Axis.BusinessObjects.Application;

namespace Axis.Api.Endpoints;

public sealed record SaveUnpublishedBusinessObjectDefinitionRequest(
    int ExpectedRevision,
    string Name,
    IReadOnlyList<BusinessObjectFieldDefinitionInput> Fields);
