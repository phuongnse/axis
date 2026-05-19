using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Endpoints;

public record AddDataClassFieldRequest(
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    FieldConfig Config);
