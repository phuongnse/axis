using System.Text.Json;
using Axis.Agent.Models;

namespace Axis.Agent.Tests;

internal static class ModelTestData
{
    public static ModelBinding Binding(ModelBindingKind kind, string? endpoint = null, string? key = "sk-test-key", int maxOutputTokens = 4000) => new()
    {
        Name = "main",
        Kind = kind,
        Endpoint = endpoint,
        Model = "model-1",
        Key = key,
        MaxOutputTokens = maxOutputTokens,
    };

    /// <summary>A call with a tool, a user message, an assistant tool call and a tool result.</summary>
    public static ModelCall Call(int? maxOutputTokens = null) => new(
        "You build applications.",
        [
            new ModelMessage(ModelRole.User, [new ModelText("Add a field.")]),
            new ModelMessage(ModelRole.Assistant, [new ModelText("Looking."), new ModelToolCall("call-1", "get_form", Json("""{"id":"f1"}"""))]),
            new ModelMessage(ModelRole.User, [new ModelToolResult("call-1", "the form", IsError: false)]),
        ],
        [new ModelTool("get_form", "Reads a form.", Json("""{"type":"object","properties":{"id":{"type":"string"}}}"""))],
        maxOutputTokens);

    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
