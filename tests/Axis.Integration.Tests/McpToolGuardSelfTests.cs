using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Integration.Tests;

/// <summary>
/// Runs <see cref="McpToolGuard"/> against fake tools that break the rule on purpose, to show that the guard reports
/// exactly those tools. No server runs.
/// </summary>
public sealed class McpToolGuardSelfTests
{
    private const string Marker = "guard-self-marker";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tool_whose_output_holds_a_seeded_value_is_reported_and_a_clean_tool_is_not()
    {
        var failures = await FailuresAsync(
            ["leaky", "clean"],
            new Dictionary<string, string> { ["leaky"] = "{}", ["clean"] = "{}" },
            [Marker],
            tool => tool == "leaky"
                ? Result(new JsonObject { ["structuredContent"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["name"] = $"x {Marker} y" }) }, ["isError"] = false })
                : Result(new JsonObject { ["structuredContent"] = new JsonObject { ["items"] = new JsonArray() }, ["isError"] = false }));

        Assert.Equal([$"leaky answered the seeded value {Marker}"], failures);
    }

    [Fact]
    public async Task A_seeded_value_that_json_escapes_is_reported_when_it_is_only_inside_the_content_text()
    {
        const string escaped = "guard+marker";
        var text = JsonSerializer.Serialize(new { name = escaped });
        Assert.DoesNotContain(escaped, text, StringComparison.Ordinal);

        var failures = await FailuresAsync(
            ["leaky"],
            new Dictionary<string, string> { ["leaky"] = "{}" },
            [escaped],
            _ => Result(new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["isError"] = false,
            }));

        Assert.Equal([$"leaky answered the seeded value {escaped}"], failures);
    }

    [Fact]
    public async Task A_tool_with_no_input_is_reported()
    {
        var failures = await FailuresAsync(
            ["listed"],
            new Dictionary<string, string>(),
            [Marker],
            _ => Result(new JsonObject { ["isError"] = false }));

        Assert.Equal(["listed has no input, so its output was not checked: give the guard an input for each tool"], failures);
    }

    [Fact]
    public async Task A_tool_that_answers_a_tool_error_or_a_jsonrpc_error_is_reported()
    {
        var failures = await FailuresAsync(
            ["failing", "refusing"],
            new Dictionary<string, string> { ["failing"] = "{}", ["refusing"] = "{}" },
            [Marker],
            tool => tool == "failing"
                ? Result(new JsonObject { ["content"] = new JsonArray(), ["isError"] = true })
                : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 3, ["error"] = new JsonObject { ["code"] = -32602, ["message"] = "The call names no tool." } });

        Assert.Equal(
            ["failing answered a tool error, so its output was not checked", "refusing answered a tool error, so its output was not checked"],
            failures);
    }

    private static Task<IReadOnlyList<string>> FailuresAsync(
        IReadOnlyList<string> tools,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyList<string> values,
        Func<string, JsonNode> answer) =>
        McpToolGuard.LeakFailuresAsync(tools, inputs, (tool, _) => Task.FromResult(answer(tool)), values, CancellationToken);

    private static JsonObject Result(JsonObject result) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 3, ["result"] = result };
}
