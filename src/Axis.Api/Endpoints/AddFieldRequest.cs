using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Endpoints;

public record AddFieldRequest(
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    FieldConfig Config);
