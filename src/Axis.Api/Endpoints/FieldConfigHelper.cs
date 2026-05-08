using System.Text.Json;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Endpoints;

internal static class FieldConfigHelper
{
    // Deserializes the polymorphic FieldConfig based on the FieldType discriminator.
    // The config property is a raw JsonElement because System.Text.Json cannot automatically
    // pick the right subtype without a custom converter.
    internal static FieldConfig Deserialize(FieldType type, JsonElement config)
    {
        var json = config.GetRawText();
        return type switch
        {
            FieldType.Text => JsonSerializer.Deserialize<TextFieldConfig>(json)!,
            FieldType.Number => JsonSerializer.Deserialize<NumberFieldConfig>(json)!,
            FieldType.Boolean => JsonSerializer.Deserialize<BooleanFieldConfig>(json)!,
            FieldType.Date => JsonSerializer.Deserialize<DateFieldConfig>(json)!,
            FieldType.Enum => JsonSerializer.Deserialize<EnumFieldConfig>(json)!,
            FieldType.Relation => JsonSerializer.Deserialize<RelationFieldConfig>(json)!,
            FieldType.DataClass => JsonSerializer.Deserialize<DataClassFieldConfig>(json)!,
            FieldType.File => JsonSerializer.Deserialize<FileFieldConfig>(json)!,
            FieldType.Json => new JsonFieldConfig(),
            _ => throw new NotSupportedException($"Unsupported field type: {type}")
        };
    }
}
