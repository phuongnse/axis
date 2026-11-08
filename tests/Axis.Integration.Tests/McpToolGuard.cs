using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Integration.Tests;

/// <summary>
/// Holds every MCP tool to the rule in docs/reference/mcp-server.md#tools: a tool's output holds no record value, secret or
/// binding value. It calls each tool that <c>tools/list</c> offers and reads every string of the parsed answer. A string
/// that holds JSON, such as the text of a tool's content, is parsed and read the same way, so a value that the inner JSON
/// escapes is found as well. A tool the guard has no input for is a failure, so a tool
/// added later is not left unchecked.
/// </summary>
internal static class McpToolGuard
{
    /// <summary>
    /// Calls each of <paramref name="tools"/> with its arguments from <paramref name="inputs"/> through <paramref name="call"/>,
    /// which returns the JSON-RPC answer. Returns one line for each tool without an input, one for each tool that answers a
    /// tool error, and one for each of <paramref name="values"/> an answer holds.
    /// </summary>
    public static async Task<IReadOnlyList<string>> LeakFailuresAsync(
        IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, string> inputs,
        Func<string, string, Task<JsonNode>> call,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var tool in tools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!inputs.TryGetValue(tool, out var arguments))
            {
                failures.Add($"{tool} has no input, so its output was not checked: give the guard an input for each tool");
                continue;
            }

            var answer = await call(tool, arguments);
            if (answer["error"] is not null || answer["result"]?["isError"]?.GetValue<bool>() == true)
            {
                failures.Add($"{tool} answered a tool error, so its output was not checked");
                continue;
            }

            var strings = Strings(answer).ToList();
            failures.AddRange(values
                .Where(value => strings.Any(text => text.Contains(value, StringComparison.Ordinal)))
                .Select(value => $"{tool} answered the seeded value {value}"));
        }

        return failures;
    }

    // Every string value and every property name of the node, at any depth, and those of a string that holds a JSON object or array.
    private static IEnumerable<string> Strings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (name, child) in properties)
                {
                    yield return name;
                    foreach (var text in Strings(child))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonArray items:
                foreach (var child in items)
                {
                    foreach (var text in Strings(child))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                yield return text;
                if (Nested(text) is { } nested)
                {
                    foreach (var inner in Strings(nested))
                    {
                        yield return inner;
                    }
                }

                break;
        }
    }

    private static JsonNode? Nested(string text)
    {
        if (!text.StartsWith('{') && !text.StartsWith('['))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
