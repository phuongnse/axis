using System.Text.Json;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

internal static class FormFieldConfigDeserializer
{
    internal static FormFieldConfig? Deserialize(FormFieldType type, JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        string json = element.GetRawText();
        return type switch
        {
            FormFieldType.Text => JsonSerializer.Deserialize<TextFormFieldConfig>(json, options),
            FormFieldType.Number => JsonSerializer.Deserialize<NumberFormFieldConfig>(json, options),
            FormFieldType.Boolean => null,
            FormFieldType.Date => JsonSerializer.Deserialize<DateFormFieldConfig>(json, options),
            FormFieldType.Dropdown => JsonSerializer.Deserialize<DropdownFieldConfig>(json, options),
            FormFieldType.MultiSelect => JsonSerializer.Deserialize<MultiSelectFieldConfig>(json, options),
            FormFieldType.RelationPicker => JsonSerializer.Deserialize<RelationPickerFieldConfig>(json, options),
            FormFieldType.FileUpload => JsonSerializer.Deserialize<FileUploadFieldConfig>(json, options),
            FormFieldType.Section => JsonSerializer.Deserialize<SectionFieldConfig>(json, options),
            _ => throw new JsonException($"Unsupported form field type: {type}")
        };
    }
}
