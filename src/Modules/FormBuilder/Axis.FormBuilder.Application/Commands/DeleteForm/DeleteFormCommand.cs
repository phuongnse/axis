using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.DeleteForm;

/// <summary>Soft-delete a form definition. Blocked if referenced by active/draft workflows.</summary>
public sealed record DeleteFormCommand(Guid FormId, Guid workspaceId) : ICommand;
