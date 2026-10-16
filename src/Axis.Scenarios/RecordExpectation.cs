using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;

namespace Axis.Scenarios;

/// <summary>
/// Compares the values an <c>expectRecord</c> step names with a saved record. Numbers compare by
/// value, so 5000 equals 5000.00, and date-times compare as instants. A child collection must have
/// as many rows as expected, and each expected row names only the properties to check, because a
/// scenario cannot know the ids of the rows.
/// </summary>
internal static class RecordExpectation
{
    /// <summary>The first difference as <c>field 'x' expected … but was …</c>, or null when the record holds the values.</summary>
    public static string? FirstDifference(
        ApplicationModel model,
        EntityModel entity,
        IReadOnlyDictionary<string, JsonNode?> actual,
        IReadOnlyDictionary<string, JsonElement> expected,
        string prefix = "")
    {
        foreach (var (name, value) in expected)
        {
            if (!entity.TryGetField(name, out var field))
            {
                return $"field '{prefix}{name}' is not a field of {entity.Name}";
            }

            actual.TryGetValue(field.Name, out var actualValue);
            var path = prefix + field.Name;
            var difference = field.Type == FieldType.ChildCollection
                ? Rows(model, field, path, actualValue, value)
                : Equal(field.Type, value, actualValue) ? null : Mismatch(path, value.GetRawText(), actualValue);
            if (difference is not null)
            {
                return difference;
            }
        }

        return null;
    }

    private static string? Rows(ApplicationModel model, FieldModel field, string path, JsonNode? actual, JsonElement expected)
    {
        var rows = actual as JsonArray ?? [];
        if (expected.ValueKind != JsonValueKind.Array)
        {
            return Mismatch(path, expected.GetRawText(), actual);
        }

        if (expected.GetArrayLength() != rows.Count)
        {
            return $"field '{path}' expected {expected.GetArrayLength()} row(s) but was {rows.Count} row(s)";
        }

        var child = model.Entities.Single(candidate => candidate.Id == field.Target!.Id);
        var index = 0;
        foreach (var expectedRow in expected.EnumerateArray())
        {
            var rowPath = $"{path}/{index}/";
            if (expectedRow.ValueKind != JsonValueKind.Object || rows[index] is not JsonObject actualRow)
            {
                return Mismatch($"{path}/{index}", expectedRow.GetRawText(), rows[index]);
            }

            var expectedValues = expectedRow.EnumerateObject().ToDictionary(property => property.Name, property => property.Value);
            var actualValues = actualRow.ToDictionary(property => property.Key, property => property.Value);
            if (FirstDifference(model, child, actualValues, expectedValues, rowPath) is { } difference)
            {
                return difference;
            }

            index++;
        }

        return null;
    }

    private static bool Equal(FieldType type, JsonElement expected, JsonNode? actual)
    {
        var actualValue = JsonSerializer.SerializeToElement(actual);
        if (type == FieldType.DateTime
            && expected.ValueKind == JsonValueKind.String
            && actualValue.ValueKind == JsonValueKind.String
            && TryParseInstant(expected.GetString(), out var expectedInstant)
            && TryParseInstant(actualValue.GetString(), out var actualInstant))
        {
            return expectedInstant == actualInstant;
        }

        return JsonElement.DeepEquals(expected, actualValue);
    }

    private static bool TryParseInstant(string? text, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out instant);

    private static string Mismatch(string path, string expected, JsonNode? actual) =>
        $"field '{path}' expected {expected} but was {actual?.ToJsonString() ?? "null"}";
}
