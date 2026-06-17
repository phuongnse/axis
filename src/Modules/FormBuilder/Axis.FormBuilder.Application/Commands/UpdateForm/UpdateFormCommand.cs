using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.UpdateForm;

public sealed record UpdateFormCommand(
    Guid FormId,
    Guid workspaceId,
    string Name,
    string? Description) : ICommand;
