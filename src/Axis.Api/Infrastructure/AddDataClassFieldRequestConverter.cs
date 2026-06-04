using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Api.Endpoints;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;

namespace Axis.Api.Infrastructure;

public sealed class AddDataClassFieldRequestConverter : JsonConverter<AddDataClassFieldRequest>
{
    public override AddDataClassFieldRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        string name = root.GetProperty("name").GetString()!;
        string label = root.GetProperty("label").GetString()!;
        FieldType type = JsonSerializer.Deserialize<FieldType>(root.GetProperty("type").GetRawText(), options);
        bool isRequired = root.GetProperty("isRequired").GetBoolean();
        JsonElement configEl = root.TryGetProperty("config", out JsonElement c) ? c : default;
        FieldConfig config = FieldConfigDeserializer.Deserialize(type, configEl, options);

        return new AddDataClassFieldRequest(name, label, type, isRequired, config);
    }

    public override void Write(Utf8JsonWriter writer, AddDataClassFieldRequest value, JsonSerializerOptions options)
        => throw new NotSupportedException($"{nameof(AddDataClassFieldRequest)} is a request-only type.");
}
