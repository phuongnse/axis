using System.Net;
using System.Text.Json;
using Axis.Agent.Models;
using Microsoft.Extensions.Logging.Abstractions;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class AnthropicModelAdapterTests
{
    private const string Recorded = """
        {
          "id": "msg_1",
          "type": "message",
          "role": "assistant",
          "content": [
            { "type": "text", "text": "I will read it." },
            { "type": "tool_use", "id": "toolu_9", "name": "get_form", "input": { "id": "f2" } }
          ],
          "stop_reason": "tool_use",
          "usage": { "input_tokens": 100, "cache_creation_input_tokens": 20, "cache_read_input_tokens": 3, "output_tokens": 42 }
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_has_the_shape_of_the_messages_api_and_the_answer_has_the_tool_call_and_the_tokens()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, Recorded);
        var adapter = Adapter(handler, Binding(ModelBindingKind.Anthropic, "https://models.example/"));

        var answer = await adapter.CallAsync(Call(maxOutputTokens: 1000), CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://models.example/v1/messages", request.Uri.ToString());
        Assert.Equal("sk-test-key", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("model-1", root.GetProperty("model").GetString());
        Assert.Equal(1000, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("You build applications.", root.GetProperty("system").GetString());
        var tool = Assert.Single(root.GetProperty("tools").EnumerateArray());
        Assert.Equal("get_form", tool.GetProperty("name").GetString());
        Assert.Equal("Reads a form.", tool.GetProperty("description").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        var messages = root.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["user", "assistant", "user"], messages.Select(message => message.GetProperty("role").GetString()));
        Assert.Equal("text", messages[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("Add a field.", messages[0].GetProperty("content")[0].GetProperty("text").GetString());
        var toolUse = messages[1].GetProperty("content")[1];
        Assert.Equal(("tool_use", "call-1", "get_form", "f1"), (toolUse.GetProperty("type").GetString(), toolUse.GetProperty("id").GetString(), toolUse.GetProperty("name").GetString(), toolUse.GetProperty("input").GetProperty("id").GetString()));
        var toolResult = messages[2].GetProperty("content")[0];
        Assert.Equal(("tool_result", "call-1", "the form", false), (toolResult.GetProperty("type").GetString(), toolResult.GetProperty("tool_use_id").GetString(), toolResult.GetProperty("content").GetString(), toolResult.GetProperty("is_error").GetBoolean()));

        Assert.Equal(ModelStopReason.ToolUse, answer.StopReason);
        Assert.Equal(new ModelTokens(123, 42), answer.Tokens);
        Assert.Equal("I will read it.", Assert.IsType<ModelText>(answer.Content[0]).Text);
        var toolCall = Assert.IsType<ModelToolCall>(answer.Content[1]);
        Assert.Equal(("toolu_9", "get_form", "f2"), (toolCall.Id, toolCall.Name, toolCall.Input.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task An_empty_endpoint_calls_the_anthropic_api_and_the_limit_is_the_bindings_at_most()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, Recorded);
        var adapter = Adapter(handler, Binding(ModelBindingKind.Anthropic, maxOutputTokens: 4000));

        await adapter.CallAsync(Call(maxOutputTokens: 90000), CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.Uri.ToString());
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal(4000, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task A_binding_with_no_key_sends_no_key_header()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, Recorded);
        var adapter = Adapter(handler, Binding(ModelBindingKind.Anthropic, "http://localhost:8080", key: null));

        await adapter.CallAsync(Call(), CancellationToken);

        Assert.False(Assert.Single(handler.Requests).Headers.ContainsKey("x-api-key"));
    }

    [Fact]
    public async Task An_answer_that_cannot_be_read_fails_with_a_message_that_names_the_binding_and_no_body()
    {
        var handler = new RecordingHandler().Answer(HttpStatusCode.OK, """{"echo":"body-text-1"}""");
        var adapter = Adapter(handler, Binding(ModelBindingKind.Anthropic));

        var exception = await Assert.ThrowsAsync<ModelCallException>(() => adapter.CallAsync(Call(), CancellationToken));

        Assert.Contains("'main'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("body-text-1", exception.ToString(), StringComparison.Ordinal);
    }

    private static AnthropicModelAdapter Adapter(RecordingHandler handler, ModelBinding binding) =>
        new(binding, new HttpClient(handler), TimeProvider.System, NullLogger.Instance);
}
