using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.AddField;

/// <summary>Add a field of any supported type to a model.</summary>
public sealed record AddFieldCommand(
    Guid ModelId,
    Guid workspaceId,
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    FieldConfig Config) : ICommand<Guid>;
