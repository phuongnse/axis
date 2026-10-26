using System.Text.Json.Nodes;
using Axis.Configuration.Model;

namespace Axis.Server.Exports;

/// <summary>
/// The machine form of a value in an export file: ISO dates and instants, numbers as text with a dot
/// and every stored digit, enum values and not their labels, <c>true</c> and <c>false</c>, a file as
/// its name, and a reference as the display value of its target.
/// </summary>
internal static class ExportValues
{
    private const string ReferencesSeparator = "; ";

    /// <summary>
    /// The cell text of <paramref name="value"/>, or null for no value. <paramref name="label"/> is the
    /// display value of a reference, or of each target of a <c>references</c> field as a <see cref="JsonArray"/>
    /// in the order of the ids; a missing label falls back to the target's record id.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is a child collection, which is never a column.</exception>
    public static string? Format(FieldType type, JsonNode? value, JsonNode? label)
    {
        if (value is null)
        {
            return null;
        }

        return type switch
        {
            FieldType.Text or FieldType.Enum or FieldType.Date or FieldType.DateTime => value.GetValue<string>(),

            // The number text is written as is, so trailing zeros and every digit are kept.
            FieldType.Integer or FieldType.Decimal or FieldType.Money => value.ToJsonString(),
            FieldType.Boolean => value.GetValue<bool>() ? "true" : "false",
            FieldType.Reference => Text(label) ?? value.GetValue<string>(),
            FieldType.References => string.Join(
                ReferencesSeparator,
                value.AsArray().Select((id, index) => Text((label as JsonArray)?.ElementAtOrDefault(index)) ?? id?.GetValue<string>())),
            FieldType.File => value["name"]?.GetValue<string>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "This field type is never an export column."),
        };
    }

    /// <summary>Whether a cell of <paramref name="type"/> is a number, which no formula character can start.</summary>
    public static bool IsNumeric(FieldType type) => type is FieldType.Integer or FieldType.Decimal or FieldType.Money;

    private static string? Text(JsonNode? node) =>
        node is JsonValue text && text.TryGetValue<string>(out var result) ? result : null;
}
