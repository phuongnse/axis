using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;

namespace Axis.Configuration.Packages;

/// <summary>
/// The one codec between the .NET values of an operation's input and output and the JSON values of the
/// runner protocol, see docs/reference/extension-packages.md#the-runner-protocol. <c>text</c> is a string,
/// <c>integer</c> and <c>decimal</c> are invariant strings so that no language loses precision,
/// <c>boolean</c> is a JSON boolean, <c>date</c> is <c>yyyy-MM-dd</c> and <c>date-time</c> is ISO 8601 with an
/// offset. A missing value is <see langword="null"/>.
/// </summary>
public static class RunnerValues
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Writes the values of the manifest fields that <paramref name="values"/> holds. A value of another
    /// .NET type than its field's throws <see cref="InvalidCastException"/>.
    /// </summary>
    public static JsonObject Encode(IReadOnlyList<PackageField> fields, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);

        var result = new JsonObject();
        foreach (var field in fields)
        {
            if (values.TryGetValue(field.Name, out var value))
            {
                result[field.Name] = value is null ? null : EncodeValue(field, value);
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the manifest fields from <paramref name="values"/>, the inverse of <see cref="Encode"/>. A field
    /// that <paramref name="values"/> lacks is <see langword="null"/>.
    /// </summary>
    /// <exception cref="FormatException">A value is not in the format of its field's type.</exception>
    public static Dictionary<string, object?> Decode(IReadOnlyList<PackageField> fields, JsonObject values)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            result[field.Name] = values.TryGetPropertyValue(field.Name, out var node) && node is not null
                ? DecodeValue(field, node)
                : null;
        }

        return result;
    }

    private static JsonValue EncodeValue(PackageField field, object value) =>
        (field.Type, value) switch
        {
            (FieldType.Text, string text) => JsonValue.Create(text),
            (FieldType.Integer, long integer) => JsonValue.Create(integer.ToString(CultureInfo.InvariantCulture)),
            (FieldType.Decimal, decimal number) => JsonValue.Create(number.ToString(CultureInfo.InvariantCulture)),
            (FieldType.Boolean, bool flag) => JsonValue.Create(flag),
            (FieldType.Date, DateOnly date) => JsonValue.Create(date.ToString(DateFormat, CultureInfo.InvariantCulture)),
            (FieldType.DateTime, DateTimeOffset time) => JsonValue.Create(time.ToString("O", CultureInfo.InvariantCulture)),
            _ => throw new InvalidCastException($"The value of the field '{field.Name}' is not of the .NET type of a '{FieldTypes.Name(field.Type)}' field."),
        };

    private static object DecodeValue(PackageField field, JsonNode node)
    {
        var kind = node.GetValueKind();
        if (field.Type == FieldType.Boolean)
        {
            return kind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Malformed(field),
            };
        }

        if (kind != JsonValueKind.String)
        {
            throw Malformed(field);
        }

        var text = node.GetValue<string>();
        return field.Type switch
        {
            FieldType.Text => text,
            FieldType.Integer => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer) ? integer : throw Malformed(field),
            FieldType.Decimal => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : throw Malformed(field),
            FieldType.Date => DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw Malformed(field),
            FieldType.DateTime => ParseDateTime(field, text),
            _ => throw Malformed(field),
        };
    }

    private static DateTimeOffset ParseDateTime(PackageField field, string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) && HasOffset(text)
            ? time
            : throw Malformed(field);

    /// <summary>Whether an ISO 8601 date-time ends with <c>Z</c> or a <c>+hh:mm</c> or <c>-hh:mm</c> offset.</summary>
    private static bool HasOffset(string text) =>
        text.EndsWith('Z') || text.EndsWith('z') || (text.Length > 6 && (text[^6] is '+' or '-') && text[^3] == ':');

    /// <summary>The error names the field, never the value, because a value can be a secret.</summary>
    private static FormatException Malformed(PackageField field) =>
        new($"The value of the field '{field.Name}' is not a valid '{FieldTypes.Name(field.Type)}' value.");
}
