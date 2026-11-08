using Axis.Agent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class ScriptedModelAdapterTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

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
        }).Build());
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
        }).Build());
        await using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ModelAdapters>().For("missing-binding"));

        Assert.Contains("'Ai:Bindings'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("missing-binding", exception.Message, StringComparison.Ordinal);
    }

    private static ModelAnswer Answer(string text) => new([new ModelText(text)], ModelStopReason.EndTurn, new ModelTokens(1, 1));

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Axis.Agent.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
