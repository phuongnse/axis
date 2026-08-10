using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.Api.Solutions;

internal static partial class CanonicalSolutionComponentJson
{
    public static JsonDocument Parse(ReadOnlySpan<byte> bytes)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            RejectDuplicatesAndValidate(document.RootElement);
            byte[] encoded = Encode(document.RootElement);
            if (!bytes.SequenceEqual(encoded))
            {
                document.Dispose();
                throw new InvalidOperationException("Solution component JSON is not canonical.");
            }
            return document;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Solution component JSON is invalid.", exception);
        }
    }

    private static void RejectDuplicatesAndValidate(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                HashSet<string> names = new(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new InvalidOperationException("Solution component JSON contains a duplicate property.");
                    ValidateString(property.Name);
                    RejectDuplicatesAndValidate(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    RejectDuplicatesAndValidate(item);
                break;
            case JsonValueKind.String:
                ValidateString(element.GetString()!);
                break;
            case JsonValueKind.Number:
                if (!IntegerPattern().IsMatch(element.GetRawText()))
                    throw new InvalidOperationException("Solution component numbers must be canonical integers.");
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                throw new InvalidOperationException("Solution component JSON cannot contain null values.");
        }
    }

    private static void ValidateString(string value)
    {
        if (value != value.Trim() || !value.IsNormalized(NormalizationForm.FormC))
            throw new InvalidOperationException("Solution component strings must be trimmed NFC values.");
    }

    private static byte[] Encode(JsonElement element)
    {
        ArrayBufferWriter<byte> buffer = new();
        using Utf8JsonWriter writer = new(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        });
        Write(writer, element);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                throw new InvalidOperationException("Solution component JSON value is unsupported.");
        }
    }

    [GeneratedRegex("\\A(?:0|-?[1-9][0-9]*)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerPattern();
}
