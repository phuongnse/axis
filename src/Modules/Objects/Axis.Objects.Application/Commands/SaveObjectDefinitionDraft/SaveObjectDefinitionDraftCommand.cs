using Axis.Shared.Application.CQRS;

namespace Axis.Objects.Application.Commands.SaveObjectDefinitionDraft;

public sealed record SaveObjectDefinitionDraftCommand(
    Guid ObjectDefinitionId,
    int ExpectedDraftVersion,
    string Name,
    IReadOnlyList<ObjectFieldDefinitionInput> Fields)
    : ICommand<ObjectDefinitionDetailDto>;
