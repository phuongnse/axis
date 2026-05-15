using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.UpdateForm;

public sealed record UpdateFormCommand(
    Guid FormId,
    Guid OrganizationId,
    string Name,
    string? Description) : ICommand;
