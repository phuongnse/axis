using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Agent.Models;

namespace Axis.Agent;

/// <summary>How a <see cref="ModelLoop"/> ended.</summary>
internal enum ModelLoopEnd
{
    /// <summary>The model ended its turn.</summary>
    EndTurn,

    /// <summary>An answer stopped at the output limit, or for a reason the loop does not know.</summary>
    CutOff,

    /// <summary>The model did not end its turn within the calls it was allowed.</summary>
    OutOfCalls,
}

/// <summary>What a <see cref="ModelLoop"/> leaves: how it ended, the last answer and every message as JSON.</summary>
internal sealed record ModelLoopResult(ModelLoopEnd End, ModelAnswer? Last, string Messages);

/// <summary>
/// The one loop of model calls that the steps of the agent host share, see <c>docs/reference/authoring-requests.md#states</c> and
/// <c>docs/reference/authoring-requests.md#budgets</c>. A step offers the model some tools of the MCP server, and the loop calls the model up to a bound,
/// answers each tool call through the step and adds the tokens of every call to the request.
/// </summary>
internal static class ModelLoop
{
    /// <summary>Runs the model until it ends its turn, is cut off, or has made <paramref name="maxCalls"/> calls.</summary>
    /// <param name="answer">The step's answer to one tool call. A tool error is a result with <see cref="ModelToolResult.IsError"/>.</param>
    /// <exception cref="AuthoringClaimLostException">The host no longer holds the claim.</exception>
    internal static async Task<ModelLoopResult> RunAsync(
        IModelAdapter model,
        AuthoringStepContext context,
        string system,
        string firstMessage,
        IReadOnlyList<ModelTool> tools,
        int maxCalls,
        Func<ModelToolCall, CancellationToken, Task<ModelToolResult>> answer,
        CancellationToken cancellationToken)
    {
        var messages = new List<ModelMessage> { new(ModelRole.User, [new ModelText(firstMessage)]) };
        ModelAnswer? last = null;
        for (var call = 0; call < maxCalls; call++)
        {
            last = await model.CallAsync(new ModelCall(system, [.. messages], tools), cancellationToken);
            await context.AddTokensAsync((long)last.Tokens.Input + last.Tokens.Output, cancellationToken);
            messages.Add(new ModelMessage(ModelRole.Assistant, last.Content));
            switch (last.StopReason)
            {
                case ModelStopReason.ToolUse:
                    var results = new List<ModelContent>();
                    foreach (var toolCall in last.Content.OfType<ModelToolCall>())
                    {
                        results.Add(await answer(toolCall, cancellationToken));
                    }

                    // A model that says it uses a tool and names none gets an empty turn, and the call counts against the bound.
                    messages.Add(new ModelMessage(ModelRole.User, results.Count > 0 ? results : [new ModelText("No tool was called.")]));
                    break;
                case ModelStopReason.EndTurn:
                    return new ModelLoopResult(ModelLoopEnd.EndTurn, last, MessagesJson(messages));
                default:
                    return new ModelLoopResult(ModelLoopEnd.CutOff, last, MessagesJson(messages));
            }
        }

        return new ModelLoopResult(ModelLoopEnd.OutOfCalls, last, MessagesJson(messages));
    }

    /// <summary>The tools of the MCP server whose names are in <paramref name="names"/>, as the model is offered them.</summary>
    internal static async Task<IReadOnlyList<ModelTool>> ListToolsAsync(AuthoringStepContext context, IReadOnlySet<string> names, CancellationToken cancellationToken) =>
        [.. (await context.Mcp.ListToolsAsync(cancellationToken))
            .OfType<JsonObject>()
            .Where(tool => tool["name"]?.GetValue<string>() is { } name && names.Contains(name))
            .Select(tool => new ModelTool(
                tool["name"]!.GetValue<string>(),
                tool["description"]?.GetValue<string>() ?? "",
                JsonSerializer.SerializeToElement(tool["inputSchema"])))];

    // The request stores the application's id, and the tools take its name, so the step finds the name in the author's list of applications.
    internal static async Task<string?> FindApplicationNameAsync(AuthoringStepContext context, CancellationToken cancellationToken)
    {
        var listed = await context.Mcp.CallToolAsync("list_applications", null, cancellationToken);
        if (listed.IsError || listed.StructuredContent?["items"] is not JsonArray items)
        {
            return null;
        }

        foreach (var item in items.OfType<JsonObject>())
        {
            if (item["id"] is JsonValue id && id.TryGetValue<string>(out var text)
                && Guid.TryParse(text, out var applicationId) && applicationId == context.Request.ApplicationId
                && item["name"] is JsonValue name && name.TryGetValue<string>(out var applicationName))
            {
                return applicationName;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks the input of a tool call: it is an object or absent, and an <c>application</c> field names <paramref name="application"/>, ignoring letter case,
    /// as the server matches it. It answers the arguments, or the error the model gets.
    /// </summary>
    internal static (JsonObject? Arguments, string? Error) CheckInput(ModelToolCall call, string application)
    {
        if (call.Input.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined or JsonValueKind.Null))
        {
            return (null, "The input of a tool must be an object.");
        }

        var arguments = call.Input.ValueKind == JsonValueKind.Object ? JsonObject.Create(call.Input) : null;
        if (arguments is not null
            && arguments.TryGetPropertyValue("application", out var named)
            && !(named is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, application, StringComparison.OrdinalIgnoreCase)))
        {
            return (null, $"The tool may name only the application '{application}'.");
        }

        return (arguments, null);
    }

    // ModelContent has no type discriminator, so the messages are written by hand.
    private static string MessagesJson(IReadOnlyList<ModelMessage> messages) =>
        new JsonArray([.. messages.Select(message => (JsonNode)new JsonObject
        {
            ["role"] = message.Role == ModelRole.User ? "user" : "assistant",
            ["content"] = new JsonArray([.. message.Content.Select(Part)]),
        })]).ToJsonString();

    private static JsonNode Part(ModelContent content) => content switch
    {
        ModelText text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        ModelToolCall call => new JsonObject { ["type"] = "toolCall", ["id"] = call.Id, ["name"] = call.Name, ["input"] = call.Input.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(call.Input.GetRawText()) },
        ModelToolResult result => new JsonObject { ["type"] = "toolResult", ["callId"] = result.CallId, ["content"] = result.Content, ["isError"] = result.IsError },
        _ => throw new ArgumentOutOfRangeException(nameof(content), content, null),
    };
}
