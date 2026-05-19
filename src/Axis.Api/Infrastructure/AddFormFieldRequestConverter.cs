using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Api.Endpoints;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

public sealed class AddFormFieldRequestConverter : JsonConverter<AddFormFieldRequest>
{
    public override AddFormFieldRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string key = root.GetProperty("key").GetString()!;
        string label = root.GetProperty("label").GetString()!;
        FormFieldType type = JsonSerializer.Deserialize<FormFieldType>(root.GetProperty("type").GetRawText(), options);
        bool required = root.GetProperty("required").GetBoolean();
        JsonElement configEl = root.TryGetProperty("config", out JsonElement c) ? c : default;
        FormFieldConfig? config = FormFieldConfigDeserializer.Deserialize(type, configEl, options);

        return new AddFormFieldRequest(key, label, type, required, config);
    }

    public override void Write(Utf8JsonWriter writer, AddFormFieldRequest value, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(AddFormFieldRequest)} is a request-only type.");
}
