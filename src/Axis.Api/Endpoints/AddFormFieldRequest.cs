using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.Api.Endpoints;

public record AddFormFieldRequest(
    string Key,
    string Label,
    FormFieldType Type,
    bool Required,
    FormFieldConfig? Config);
