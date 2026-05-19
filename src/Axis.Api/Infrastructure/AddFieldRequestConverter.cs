using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Api.Endpoints;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

public sealed class AddFieldRequestConverter : JsonConverter<AddFieldRequest>
{
    public override AddFieldRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string name = root.GetProperty("name").GetString()!;
        string label = root.GetProperty("label").GetString()!;
        FieldType type = JsonSerializer.Deserialize<FieldType>(root.GetProperty("type").GetRawText(), options);
        bool isRequired = root.GetProperty("is_required").GetBoolean();
        JsonElement configEl = root.TryGetProperty("config", out JsonElement c) ? c : default;
        FieldConfig config = FieldConfigDeserializer.Deserialize(type, configEl, options);

        return new AddFieldRequest(name, label, type, isRequired, config);
    }

    public override void Write(Utf8JsonWriter writer, AddFieldRequest value, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(AddFieldRequest)} is a request-only type.");
}
