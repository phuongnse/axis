using System.Collections.Concurrent;
using System.Net;
using Axis.Agent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class ModelAdapterKeyTests : IDisposable
{
    private const string Key = "sk-test-secret-7";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-key-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("anthropic", "x-api-key", Key)]
    [InlineData("openai-compatible", "Authorization", "Bearer " + Key)]
    public async Task A_key_read_from_its_file_is_sent_to_the_provider_and_appears_in_no_log_line_or_exception_message(string kind, string header, string sent)
    {
        var keyFile = Path.Combine(_folder, "key");
        File.WriteAllText(keyFile, Key + "\n");
        var logs = new CapturingLoggerProvider();
        var handler = new RecordingHandler()
            .Answer(HttpStatusCode.OK, kind == "anthropic"
                ? """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":4}}"""
                : """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":4}}""")
            .Answer(HttpStatusCode.InternalServerError, $$"""{"error":"bad key {{Key}}"}""");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddAxisAgent(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = kind,
            ["Ai:Bindings:main:Endpoint"] = "http://models.example",
            ["Ai:Bindings:main:Model"] = "model-1",
            ["Ai:Bindings:main:KeyFile"] = keyFile,
            ["Ai:DefaultBinding"] = "main",
        }).Build());
        services.AddHttpClient(ModelAdapters.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();
        var adapter = provider.GetRequiredService<ModelAdapters>().For("main");

        var answer = await adapter.CallAsync(Call(), CancellationToken);
        var exception = await Assert.ThrowsAsync<ModelCallException>(() => adapter.CallAsync(Call(), CancellationToken));

        Assert.All(handler.Requests, request => Assert.Equal(sent, request.Headers[header]));
        Assert.Equal(new ModelTokens(3, 4), answer.Tokens);
        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains(logs.Lines, line => line.Contains("3 input and 4 output tokens", StringComparison.Ordinal));
        Assert.NotEmpty(logs.Lines);
        Assert.All(logs.Lines, line => Assert.DoesNotContain(Key, line, StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider.Lines.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
