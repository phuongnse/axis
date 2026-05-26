using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.FormBuilder.Infrastructure.Persistence.Converters;

internal sealed class FormFieldConverter : JsonConverter<FormField>
{
    public override FormField Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        Guid id = root.GetProperty("id").GetGuid();
        string key = root.GetProperty("key").GetString()!;
        string label = root.GetProperty("label").GetString()!;
        string? helpText = root.TryGetProperty("helpText", out JsonElement htEl) && htEl.ValueKind != JsonValueKind.Null
            ? htEl.GetString()
            : null;
        FormFieldType type = Enum.Parse<FormFieldType>(root.GetProperty("type").GetString()!, ignoreCase: true);
        bool isRequired = root.GetProperty("isRequired").GetBoolean();
        int displayOrder = root.GetProperty("displayOrder").GetInt32();

        FormFieldConfig? config = null;
        if (root.TryGetProperty("config", out JsonElement configEl) && configEl.ValueKind != JsonValueKind.Null)
            config = DeserializeConfig(type, configEl.GetRawText(), options);

        return FormField.Reconstitute(id, key, label, helpText, type, isRequired, displayOrder, config);
    }

    public override void Write(Utf8JsonWriter writer, FormField value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("key", value.Key);
        writer.WriteString("label", value.Label);
        if (value.HelpText != null)
            writer.WriteString("helpText", value.HelpText);
        else
            writer.WriteNull("helpText");
        writer.WriteString("type", value.Type.ToString());
        writer.WriteBoolean("isRequired", value.IsRequired);
        writer.WriteNumber("displayOrder", value.DisplayOrder);
        writer.WritePropertyName("config");
        if (value.Config == null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Config, value.Config.GetType(), options);
        writer.WriteEndObject();
    }

    private static FormFieldConfig? DeserializeConfig(FormFieldType type, string json, JsonSerializerOptions options)
        => type switch
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
            _ => throw new NotSupportedException($"Unknown form field type: {type}")
        };
}
