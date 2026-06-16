using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.RemoveFieldFromForm;

public sealed record RemoveFieldFromFormCommand(
    Guid FormId,
    Guid workspaceId,
    Guid FieldId) : ICommand;
