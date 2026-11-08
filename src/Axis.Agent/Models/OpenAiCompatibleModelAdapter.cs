using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Agent.Models;

/// <summary>Calls a model through the OpenAI chat completions API, as OpenAI, vLLM, Ollama and similar endpoints serve it.</summary>
internal sealed class OpenAiCompatibleModelAdapter(ModelBinding binding, HttpClient client, TimeProvider timeProvider, ILogger logger) : IModelAdapter
{
    private const string DefaultEndpoint = "https://api.openai.com/v1";

    public async Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = Body(request).ToJsonString();
        var uri = $"{(binding.Endpoint ?? DefaultEndpoint).TrimEnd('/')}/chat/completions";
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
        if (binding.Key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", binding.Key);
        }

        return request;
    }

    private JsonObject Body(ModelCall call)
    {
        var messages = new JsonArray();
        if (call.System.Length > 0)
        {
            messages.Add(new JsonObject { ["role"] = "system", ["content"] = call.System });
        }

        foreach (var message in call.Messages)
        {
            AddMessage(messages, message);
        }

        var body = new JsonObject
        {
            ["model"] = binding.Model,
            ["max_tokens"] = ModelHttp.OutputLimit(call, binding),
            ["messages"] = messages,
        };
        if (call.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(call.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
                },
            }).ToArray());
        }

        return body;
    }

    private static void AddMessage(JsonArray messages, ModelMessage message)
    {
        if (message.Role == ModelRole.User)
        {
            // Each tool result is a message of its own, and the text of the user follows them.
            foreach (var result in message.Content.OfType<ModelToolResult>())
            {
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.CallId, ["content"] = result.Content });
            }

            var text = Text(message);
            if (text.Length > 0)
            {
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = text });
            }

            return;
        }

        var assistant = new JsonObject { ["role"] = "assistant", ["content"] = Text(message) };
        var toolCalls = message.Content.OfType<ModelToolCall>().Select(toolCall => (JsonNode)new JsonObject
        {
            ["id"] = toolCall.Id,
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = toolCall.Name, ["arguments"] = toolCall.Input.GetRawText() },
        }).ToArray();
        if (toolCalls.Length > 0)
        {
            assistant["tool_calls"] = new JsonArray(toolCalls);
        }

        messages.Add(assistant);
    }

    private static string Text(ModelMessage message) => string.Concat(message.Content.OfType<ModelText>().Select(text => text.Text));

    private static ModelAnswer Answer(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var content = new List<ModelContent>();
        if (message.TryGetProperty("content", out var reply) && reply.ValueKind == JsonValueKind.String && reply.GetString() is { Length: > 0 } words)
        {
            content.Add(new ModelText(words));
        }

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolCall in toolCalls.EnumerateArray())
            {
                var function = toolCall.GetProperty("function");
                using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() is { Length: > 0 } json ? json : "{}");
                content.Add(new ModelToolCall(
                    toolCall.GetProperty("id").GetString() ?? string.Empty,
                    function.GetProperty("name").GetString() ?? string.Empty,
                    arguments.RootElement.Clone()));
            }
        }

        var stopReason = choice.GetProperty("finish_reason").GetString() switch
        {
            "stop" => ModelStopReason.EndTurn,
            "tool_calls" or "function_call" => ModelStopReason.ToolUse,
            "length" => ModelStopReason.MaxTokens,
            _ => throw new FormatException("Unknown finish reason."),
        };

        var usage = root.GetProperty("usage");
        return new ModelAnswer(content, stopReason, new ModelTokens(usage.GetProperty("prompt_tokens").GetInt32(), usage.GetProperty("completion_tokens").GetInt32()));
    }
}
