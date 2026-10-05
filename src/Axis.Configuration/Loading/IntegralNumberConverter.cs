using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Configuration.Loading;

/// <summary>
/// JSON Schema treats <c>2.0</c> as an integer, so integer properties accept any integral number
/// that the schema has already range-checked.
/// </summary>
internal sealed class IntegralNumberConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TryGetInt32(out var value))
        {
            return value;
        }

        if (reader.TryGetDecimal(out var number) && decimal.Truncate(number) == number && number is >= int.MinValue and <= int.MaxValue)
        {
            return (int)number;
        }

        throw new JsonException("Expected an integral number within the 32-bit range.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
