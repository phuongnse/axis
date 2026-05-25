using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.FormBuilder.Application.Queries.GetFormById;

public sealed record FormFieldDto(
    Guid Id,
    string Key,
    string Label,
    FormFieldType Type,
    bool Required,
    int DisplayOrder,
    FormFieldConfig? Config,
    bool IsBroken = false);
