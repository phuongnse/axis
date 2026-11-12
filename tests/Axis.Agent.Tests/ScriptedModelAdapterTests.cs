using Axis.Agent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class ScriptedModelAdapterTests : IDisposable
{
    private const string Setting = "Ai:ScriptedAnswersFile";
    private const string Marker = "4817203956";

    private readonly string _folder = Directory.CreateTempSubdirectory($"axis-agent-tests-{Marker}-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_scripted_adapter_cannot_be_registered_outside_development_and_testing(string environment)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddScriptedModelAdapter(new TestEnvironment(environment), []));

        Assert.Equal("The scripted model adapter is refused outside Development and Testing.", exception.Message);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Every_binding_returns_the_scripted_answers_in_order(string environment)
    {
        var first = Answer("first");
        var second = Answer("second");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxisAgent(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = "model-1",
            ["Ai:Bindings:other:Kind"] = "openai-compatible",
            ["Ai:Bindings:other:Model"] = "model-2",
        }).Build(), new TestEnvironment());
        services.AddScriptedModelAdapter(new TestEnvironment(environment), [first, second]);
        await using var provider = services.BuildServiceProvider();
        var adapters = provider.GetRequiredService<ModelAdapters>();
        var scripted = provider.GetRequiredService<ScriptedModelAdapter>();

        var answers = new[]
        {
            await adapters.For("main").CallAsync(Call(), CancellationToken),
            await adapters.For("other").CallAsync(Call(maxOutputTokens: 5), CancellationToken),
        };
        var exception = await Assert.ThrowsAsync<ModelCallException>(() => adapters.For("main").CallAsync(Call(), CancellationToken));

        Assert.Equal([first, second], answers);
        Assert.Equal([null, 5, null], scripted.Calls.Select(call => call.MaxOutputTokens));
        Assert.Equal("The scripted adapter has no answer left.", exception.Message);
    }

    [Fact]
    public async Task A_binding_that_does_not_exist_names_the_setting_and_not_the_name()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxisAgent(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = "model-1",
        }).Build(), new TestEnvironment());
        await using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ModelAdapters>().For("missing-binding"));

        Assert.Contains("'Ai:Bindings'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-binding", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task A_started_host_replays_the_answers_of_the_file_for_every_binding(string environment)
    {
        var file = Path.Combine(_folder, "answers.json");
        await File.WriteAllTextAsync(file, """
            [
              {"content":[{"type":"text","text":"first"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":4}},
              {"content":[{"type":"text","text":"second"},{"type":"tool_use","id":"call-1","name":"lookup","input":{"term":"axis"}}],"stop_reason":"tool_use","usage":{"input_tokens":5,"output_tokens":6}}
            ]
            """, CancellationToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxisAgent(Configuration(file), new TestEnvironment(environment));
        await using var provider = services.BuildServiceProvider();
        var adapters = provider.GetRequiredService<ModelAdapters>();

        var first = await adapters.For("main").CallAsync(Call(), CancellationToken);
        var second = await adapters.For("other").CallAsync(Call(), CancellationToken);
        var exception = await Assert.ThrowsAsync<ModelCallException>(() => adapters.For("main").CallAsync(Call(), CancellationToken));

        Assert.Equal("first", Assert.IsType<ModelText>(Assert.Single(first.Content)).Text);
        Assert.Equal((ModelStopReason.EndTurn, new ModelTokens(3, 4)), (first.StopReason, first.Tokens));
        Assert.Equal(ModelStopReason.ToolUse, second.StopReason);
        Assert.Equal(new ModelTokens(5, 6), second.Tokens);
        Assert.Equal("second", Assert.IsType<ModelText>(second.Content[0]).Text);
        var toolCall = Assert.IsType<ModelToolCall>(second.Content[1]);
        Assert.Equal(("call-1", "lookup", "axis"), (toolCall.Id, toolCall.Name, toolCall.Input.GetProperty("term").GetString()));
        Assert.Equal("The scripted adapter has no answer left.", exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_setting_stops_the_start_outside_development_and_testing(string environment)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAxisAgent(Configuration(Path.Combine(_folder, "answers.json")), new TestEnvironment(environment)));

        Assert.Contains($"'{Setting}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, exception.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(null, "cannot be read")]
    [InlineData("{}", "JSON array")]
    [InlineData("""[{"content":"text"}]""", "JSON array")]
    [InlineData("not json", "JSON array")]
    public async Task A_file_that_cannot_be_read_or_holds_no_answers_stops_the_start(string? content, string expected)
    {
        var file = Path.Combine(_folder, "answers.json");
        if (content is not null)
        {
            await File.WriteAllTextAsync(file, content, CancellationToken);
        }

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddAxisAgent(Configuration(file), new TestEnvironment()));

        Assert.Contains($"'{Setting}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, exception.Message, StringComparison.Ordinal);
        Assert.Empty(services);
    }

    private static IConfiguration Configuration(string file) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Ai:Bindings:main:Kind"] = "anthropic",
        ["Ai:Bindings:main:Model"] = "model-1",
        ["Ai:Bindings:other:Kind"] = "openai-compatible",
        ["Ai:Bindings:other:Model"] = "model-2",
        [Setting] = file,
    }).Build();

    private static ModelAnswer Answer(string text) => new([new ModelText(text)], ModelStopReason.EndTurn, new ModelTokens(1, 1));
}
