using Axis.Shared.Application.CQRS;

namespace Axis.Objects.Application.Commands.CreateObjectDefinitionDraft;

public sealed record CreateObjectDefinitionDraftCommand(string Name)
    : ICommand<ObjectDefinitionDetailDto>;
