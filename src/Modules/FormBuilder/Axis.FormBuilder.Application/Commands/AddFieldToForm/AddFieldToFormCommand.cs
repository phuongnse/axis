using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;

namespace Axis.FormBuilder.Application.Commands.AddFieldToForm;

public sealed record AddFieldToFormCommand(
    Guid FormId,
    Guid tenantId,
    string Key,
    string Label,
    FormFieldType Type,
    bool Required,
    FormFieldConfig? Config) : ICommand<Guid>;
