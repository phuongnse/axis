using Axis.Objects.Application;

namespace Axis.Api.Endpoints;

public sealed record SaveUnpublishedObjectDefinitionRequest(
    int ExpectedRevision,
    string Name,
    IReadOnlyList<ObjectFieldDefinitionInput> Fields);
