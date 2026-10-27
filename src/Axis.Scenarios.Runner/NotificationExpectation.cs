using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Scenarios.Runner;

/// <summary>
/// Compares the values an <c>expectNotification</c> step names with the raw placeholder values a
/// notification stored. Numbers compare by value, so 5000 equals 5000.00. Every other value compares
/// by exact JSON equality, because a notification stores its values raw, with dates as ISO text.
/// </summary>
internal static class NotificationExpectation
{
    /// <summary>The first difference as <c>value 'x' expected … but was …</c>, or null when the notification holds the values.</summary>
    public static string? FirstDifference(JsonObject stored, IReadOnlyDictionary<string, JsonElement> expected)
    {
        foreach (var (name, value) in expected)
        {
            if (!stored.TryGetPropertyValue(name, out var actual))
            {
                return $"has no value '{name}', expected {value.GetRawText()}";
            }

            if (!Equal(value, actual))
            {
                return $"expected value '{name}' {value.GetRawText()} but was {actual?.ToJsonString() ?? "null"}";
            }
        }

        return null;
    }

    private static bool Equal(JsonElement expected, JsonNode? actual)
    {
        var actualValue = JsonSerializer.SerializeToElement(actual);
        if (expected.ValueKind == JsonValueKind.Number
            && actualValue.ValueKind == JsonValueKind.Number
            && expected.TryGetDecimal(out var expectedNumber)
            && actualValue.TryGetDecimal(out var actualNumber))
        {
            return expectedNumber == actualNumber;
        }

        return JsonElement.DeepEquals(expected, actualValue);
    }
}
