using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.DataModeling.Application.Commands.AddFieldToDataClass;

/// <summary>/039: Add a field to a data class (Relation, DataClass, File types are blocked by domain).</summary>
public sealed record AddFieldToDataClassCommand(
    Guid DataClassId,
    Guid workspaceId,
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    FieldConfig Config) : ICommand<Guid>;
