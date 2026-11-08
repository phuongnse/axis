using System.Net;
using System.Text.Json;
using Axis.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class OpenAiCompatibleModelAdapterTests
{
    private const string Recorded = """
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "choices": [
            {
              "index": 0,
              "message": {
                "role": "assistant",
                "content": null,
                "tool_calls": [
                  { "id": "call_9", "type": "function", "function": { "name": "get_form", "arguments": "{\"id\":\"f2\"}" } },
                  { "id": "call_10", "type": "function", "function": { "name": "get_form", "arguments": "{\"id\":\"f3\"}" } }
                ]
              },
              "finish_reason": "tool_calls"
            }
          ],
          "usage": { "prompt_tokens": 120, "completion_tokens": 40, "total_tokens": 160 }
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_has_the_shape_of_the_chat_completions_api_and_the_answer_has_the_tool_calls_and_the_tokens()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, Recorded);
        var adapter = Adapter(handler, Binding(ModelBindingKind.OpenAiCompatible, "http://vllm.example/v1/"));

        var answer = await adapter.CallAsync(Call(maxOutputTokens: 1000), CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://vllm.example/v1/chat/completions", request.Uri.ToString());
        Assert.Equal("Bearer sk-test-key", request.Headers["Authorization"]);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("model-1", root.GetProperty("model").GetString());
        Assert.Equal(1000, root.GetProperty("max_tokens").GetInt32());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        var function = tool.GetProperty("function");
        Assert.Equal(("get_form", "Reads a form.", "object"), (function.GetProperty("name").GetString(), function.GetProperty("description").GetString(), function.GetProperty("parameters").GetProperty("type").GetString()));
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["system", "user", "assistant", "tool"], messages.Select(message => message.GetProperty("role").GetString()));
        Assert.Equal("You build applications.", messages[0].GetProperty("content").GetString());
        Assert.Equal("Add a field.", messages[1].GetProperty("content").GetString());
        var assistantCall = Assert.Single(messages[2].GetProperty("tool_calls").EnumerateArray());
        Assert.Equal(("call-1", "function", "get_form", """{"id":"f1"}"""), (assistantCall.GetProperty("id").GetString(), assistantCall.GetProperty("type").GetString(), assistantCall.GetProperty("function").GetProperty("name").GetString(), assistantCall.GetProperty("function").GetProperty("arguments").GetString()));
        Assert.Equal(("call-1", "the form"), (messages[3].GetProperty("tool_call_id").GetString(), messages[3].GetProperty("content").GetString()));

        Assert.Equal(ModelStopReason.ToolUse, answer.StopReason);
        Assert.Equal(new ModelTokens(120, 40), answer.Tokens);
        var toolCalls = answer.Content.Cast<ModelToolCall>().ToList();
        Assert.Equal([("call_9", "f2"), ("call_10", "f3")], toolCalls.Select(toolCall => (toolCall.Id, toolCall.Input.GetProperty("id").GetString())));
    }

    [Fact]
    public async Task An_empty_endpoint_calls_the_openai_api_and_the_text_of_a_final_answer_is_returned()
    {
        var handler = new RecordingHandler().Answer(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Done."},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":2}}""");
        var adapter = Adapter(handler, Binding(ModelBindingKind.OpenAiCompatible, maxOutputTokens: 4000));

        var answer = await adapter.CallAsync(Call(maxOutputTokens: 90000), CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/chat/completions", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(4000, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(ModelStopReason.EndTurn, answer.StopReason);
        Assert.Equal("Done.", Assert.IsType<ModelText>(Assert.Single(answer.Content)).Text);
    }

    [Fact]
    public async Task A_binding_with_no_key_sends_no_authorization_header()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, Recorded);
        var adapter = Adapter(handler, Binding(ModelBindingKind.OpenAiCompatible, "http://localhost:11434/v1", key: null));

        await adapter.CallAsync(Call(), CancellationToken);

        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("Authorization"));
    }

    private static OpenAiCompatibleModelAdapter Adapter(RecordingHandler handler, ModelBinding binding) =>
        new(binding, new HttpClient(handler), TimeProvider.System, NullLogger.Instance);
}
