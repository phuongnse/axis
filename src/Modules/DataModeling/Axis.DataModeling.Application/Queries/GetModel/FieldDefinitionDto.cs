using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.DataModeling.Application.Queries.GetModel;

public sealed record FieldDefinitionDto(
    Guid Id,
    string Name,
    string Label,
    string? HelpText,
    string Type,
    bool IsRequired,
    bool IsSystem,
    int DisplayOrder,
    FieldConfig Config);
