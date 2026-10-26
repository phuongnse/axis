using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Loading;

/// <summary>
/// Reads a <c>groupBy</c> entry, which is a plain name or an object with a <c>field</c> and a
/// <c>bucket</c>. The schema has already checked its shape.
/// </summary>
internal sealed class DataSourceGroupConverter : JsonConverter<DataSourceGroupDefinition>
{
    public override DataSourceGroupDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new() { Field = reader.GetString()! };
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("field", out var field) || field.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Expected a name or an object with a 'field'.");
        }

        return new()
        {
            Field = field.GetString()!,
            Bucket = root.TryGetProperty("bucket", out var bucket) && bucket.ValueKind == JsonValueKind.String ? bucket.GetString() : null,
        };
    }

    public override void Write(Utf8JsonWriter writer, DataSourceGroupDefinition value, JsonSerializerOptions options)
    {
        if (value.Bucket is null)
        {
            writer.WriteStringValue(value.Field);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("field", value.Field);
        writer.WriteString("bucket", value.Bucket);
        writer.WriteEndObject();
    }
}
