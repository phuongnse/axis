using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Api.Endpoints;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

public sealed class UpdateFieldRequestConverter : JsonConverter<UpdateFieldRequest>
{
    public override UpdateFieldRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        FieldType type = JsonSerializer.Deserialize<FieldType>(root.GetProperty("type").GetRawText(), options);
        string label = root.GetProperty("label").GetString()!;
        string? helpText = root.TryGetProperty("help_text", out JsonElement helpEl) ? helpEl.GetString() : null;
        bool isRequired = root.GetProperty("is_required").GetBoolean();
        JsonElement configEl = root.TryGetProperty("config", out JsonElement c) ? c : default;
        FieldConfig config = FieldConfigDeserializer.Deserialize(type, configEl, options);

        return new UpdateFieldRequest(type, label, helpText, isRequired, config);
    }

    public override void Write(Utf8JsonWriter writer, UpdateFieldRequest value, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(UpdateFieldRequest)} is a request-only type.");
}
