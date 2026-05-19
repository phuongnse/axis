using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Endpoints;

public record UpdateFieldRequest(
    FieldType Type,
    string Label,
    string? HelpText,
    bool IsRequired,
    FieldConfig Config);
