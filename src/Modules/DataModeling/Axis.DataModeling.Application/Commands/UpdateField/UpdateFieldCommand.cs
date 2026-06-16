using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.UpdateField;

/// <summary>/034: Update label, help text, required flag, and config of an existing field.</summary>
public sealed record UpdateFieldCommand(
    Guid ModelId,
    Guid FieldId,
    Guid OrganizationId,
    string Label,
    string? HelpText,
    bool IsRequired,
    FieldConfig Config) : ICommand;
