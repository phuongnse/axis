using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.CreateForm;

/// <summary>Create a new form definition.</summary>
public sealed record CreateFormCommand(
    string Name,
    string? Description,
    Guid workspaceId,
    string CreatedBy) : ICommand<Guid>;
