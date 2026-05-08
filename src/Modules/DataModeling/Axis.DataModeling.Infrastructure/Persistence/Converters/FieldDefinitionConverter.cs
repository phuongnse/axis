using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.DataModeling.Infrastructure.Persistence.Converters;

internal sealed class FieldDefinitionConverter : JsonConverter<FieldDefinition>
{
    public override FieldDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetGuid();
        var name = root.GetProperty("name").GetString()!;
        var label = root.GetProperty("label").GetString()!;
        string? helpText = null;
        if (root.TryGetProperty("helpText", out var htEl) && htEl.ValueKind != JsonValueKind.Null)
            helpText = htEl.GetString();
        var type = Enum.Parse<FieldType>(root.GetProperty("type").GetString()!);
        var isRequired = root.GetProperty("isRequired").GetBoolean();
        var isSystem = root.GetProperty("isSystem").GetBoolean();
        var displayOrder = root.GetProperty("displayOrder").GetInt32();
        var configJson = root.GetProperty("config").GetRawText();
        var config = DeserializeConfig(type, configJson, options);

        return FieldDefinition.Reconstitute(id, name, label, helpText, type, isRequired, isSystem, displayOrder, config);
    }

    public override void Write(Utf8JsonWriter writer, FieldDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("label", value.Label);
        if (value.HelpText is null) writer.WriteNull("helpText");
        else writer.WriteString("helpText", value.HelpText);
        writer.WriteString("type", value.Type.ToString());
        writer.WriteBoolean("isRequired", value.IsRequired);
        writer.WriteBoolean("isSystem", value.IsSystem);
        writer.WriteNumber("displayOrder", value.DisplayOrder);
        writer.WritePropertyName("config");
        JsonSerializer.Serialize(writer, value.Config, value.Config.GetType(), options);
        writer.WriteEndObject();
    }

    private static FieldConfig DeserializeConfig(FieldType type, string json, JsonSerializerOptions options)
        => type switch
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
            _ => throw new NotSupportedException($"Unknown field type: {type}")
        };
}
