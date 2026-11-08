using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Agent.Models;

/// <summary>Calls a model through the Anthropic Messages API, or any endpoint that speaks it.</summary>
internal sealed class AnthropicModelAdapter(ModelBinding binding, HttpClient client, TimeProvider timeProvider, ILogger logger) : IModelAdapter
{
    private const string DefaultEndpoint = "https://api.anthropic.com";

    public async Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = Body(request).ToJsonString();
        var uri = $"{(binding.Endpoint ?? DefaultEndpoint).TrimEnd('/')}/v1/messages";
        var (text, attempts) = await ModelHttp.SendAsync(client, () => Request(uri, body), binding, timeProvider, cancellationToken);

        ModelAnswer answer;
        try
        {
            answer = Answer(text);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // The exception is not kept: its message can quote the body.
            throw ModelHttp.Unreadable(binding);
        }

        ModelLog.Called(logger, binding.Name, binding.Model, answer.Tokens.Input, answer.Tokens.Output, attempts);
        return answer;
    }

    private HttpRequestMessage Request(string uri, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json")),
        };
        request.Headers.Add("anthropic-version", "2023-06-01");
        if (binding.Key is not null)
        {
            request.Headers.Add("x-api-key", binding.Key);
        }

        return request;
    }

    private JsonObject Body(ModelCall call)
    {
        var body = new JsonObject
        {
            ["model"] = binding.Model,
            ["max_tokens"] = ModelHttp.OutputLimit(call, binding),
        };
        if (call.System.Length > 0)
        {
            body["system"] = call.System;
        }

        body["messages"] = new JsonArray(call.Messages.Select(Message).ToArray());
        if (call.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(call.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
            }).ToArray());
        }

        return body;
    }

    private static JsonNode Message(ModelMessage message) => new JsonObject
    {
        ["role"] = message.Role == ModelRole.User ? "user" : "assistant",
        ["content"] = new JsonArray(message.Content.Select(Block).ToArray()),
    };

    private static JsonNode Block(ModelContent content) => content switch
    {
        ModelText text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        ModelToolCall toolCall => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = toolCall.Id,
            ["name"] = toolCall.Name,
            ["input"] = JsonNode.Parse(toolCall.Input.GetRawText()),
        },
        ModelToolResult result => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = result.CallId,
            ["content"] = result.Content,
            ["is_error"] = result.IsError,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(content)),
    };

    private static ModelAnswer Answer(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;

        var content = new List<ModelContent>();
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    content.Add(new ModelText(block.GetProperty("text").GetString() ?? string.Empty));
                    break;
                case "tool_use":
                    content.Add(new ModelToolCall(
                        block.GetProperty("id").GetString() ?? string.Empty,
                        block.GetProperty("name").GetString() ?? string.Empty,
                        block.GetProperty("input").Clone()));
                    break;
            }
        }

        var stopReason = root.GetProperty("stop_reason").GetString() switch
        {
            "end_turn" or "stop_sequence" => ModelStopReason.EndTurn,
            "tool_use" => ModelStopReason.ToolUse,
            "max_tokens" => ModelStopReason.MaxTokens,
            _ => throw new FormatException("Unknown stop reason."),
        };

        var usage = root.GetProperty("usage");
        var input = Tokens(usage, "input_tokens") + Tokens(usage, "cache_creation_input_tokens") + Tokens(usage, "cache_read_input_tokens");
        return new ModelAnswer(content, stopReason, new ModelTokens(input, Tokens(usage, "output_tokens")));
    }

    private static int Tokens(JsonElement usage, string name) =>
        usage.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
