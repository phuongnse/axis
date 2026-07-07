using Axis.Objects.Application;

namespace Axis.Api.Endpoints;

public sealed record SaveObjectDefinitionDraftRequest(
    int ExpectedDraftVersion,
    string Name,
    IReadOnlyList<ObjectFieldDefinitionInput> Fields);
