using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.DeleteForm;

/// <summary>US-078: Soft-delete a form definition. Blocked if referenced by active/draft workflows.</summary>
public sealed record DeleteFormCommand(Guid FormId, Guid OrganizationId) : ICommand;
