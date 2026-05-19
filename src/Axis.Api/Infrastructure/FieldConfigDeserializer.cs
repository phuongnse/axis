using System.Text.Json;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

internal static class FieldConfigDeserializer
{
    internal static FieldConfig Deserialize(FieldType type, JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return Default(type);

        string json = element.GetRawText();
        return type switch
        {
            FieldType.Text => JsonSerializer.Deserialize<TextFieldConfig>(json, options)!,
            FieldType.Number => JsonSerializer.Deserialize<NumberFieldConfig>(json, options)!,
            FieldType.Boolean => JsonSerializer.Deserialize<BooleanFieldConfig>(json, options)!,
            FieldType.Date => JsonSerializer.Deserialize<DateFieldConfig>(json, options)!,
            FieldType.Enum => JsonSerializer.Deserialize<EnumFieldConfig>(json, options)!,
            FieldType.Relation => JsonSerializer.Deserialize<RelationFieldConfig>(json, options)!,
            FieldType.DataClass => JsonSerializer.Deserialize<DataClassFieldConfig>(json, options)!,
            FieldType.File => JsonSerializer.Deserialize<FileFieldConfig>(json, options)!,
            FieldType.Json => new JsonFieldConfig(),
            _ => throw new JsonException($"Unsupported field type: {type}")
        };
    }

    private static FieldConfig Default(FieldType type) => type switch
    {
        FieldType.Text => new TextFieldConfig(),
        FieldType.Number => new NumberFieldConfig(),
        FieldType.Boolean => new BooleanFieldConfig(),
        FieldType.Date => new DateFieldConfig(),
        FieldType.Json => new JsonFieldConfig(),
        FieldType.File => new FileFieldConfig(),
        // These types require config fields (TargetModelId, DataClassId, Options) — null config is invalid.
        _ => throw new JsonException($"Field type '{type}' requires a config object.")
    };
}
