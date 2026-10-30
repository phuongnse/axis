using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// Reads one input of a <c>callConnector</c> step, which is an expression string or an object with
/// an optional <c>from</c> and the nested <c>fields</c>, each of them an input again. The schema has
/// already checked its shape. The nested fields are read through the document so that file order is kept.
/// </summary>
internal sealed class InputMappingConverter : JsonConverter<InputMappingDefinition>
{
    public override InputMappingDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Convert(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, InputMappingDefinition value, JsonSerializerOptions options)
    {
        if (value.Fields is null)
        {
            writer.WriteStringValue(value.Expression);
            return;
        }

        writer.WriteStartObject();
        if (value.From is not null)
        {
            writer.WriteString("from", value.From);
        }

        writer.WritePropertyName("fields");
        writer.WriteStartObject();
        foreach (var (name, field) in value.Fields)
        {
            writer.WritePropertyName(name);
            Write(writer, field, options);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static InputMappingDefinition Convert(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new() { Expression = element.GetString() };
        }

        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected an expression or an object with 'fields'.");
        }

        var nested = new Dictionary<string, InputMappingDefinition>();
        foreach (var property in fields.EnumerateObject())
        {
            nested[property.Name] = Convert(property.Value);
        }

        return new()
        {
            From = element.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.String ? from.GetString() : null,
            Fields = nested,
        };
    }
}
