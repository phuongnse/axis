using System.Net;
using Axis.Agent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Axis.Agent.Tests.ModelTestData;

namespace Axis.Agent.Tests;

public sealed class TenantModelAdaptersTests : IDisposable
{
    private const string InstallationKey = "sk-installation-key-12345678901";
    private const string TenantKey = "sk-tenant-key-12345678901";

    private const string AnthropicAnswer = """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":4}}""";
    private const string OpenAiAnswer = """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":4}}""";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-tenant-key-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Under_configuration_with_the_setting_on_a_call_sends_the_installations_key()
    {
        await using var provider = Provider("anthropic", new RecordingHandler().Answer(HttpStatusCode.OK, AnthropicAnswer), out var handler, allowTenantKeys: "true");

        var adapter = await provider.GetRequiredService<TenantModelAdapters>().ForAsync("a", "main", CancellationToken);
        await adapter.CallAsync(Call(), CancellationToken);

        Assert.Equal(InstallationKey, Assert.Single(handler.Requests).Headers["x-api-key"]);
    }

    [Theory]
    [InlineData("anthropic", "x-api-key", TenantKey, AnthropicAnswer)]
    [InlineData("openai-compatible", "Authorization", "Bearer " + TenantKey, OpenAiAnswer)]
    public async Task A_tenants_key_replaces_the_installations_key_in_the_header_of_the_kind(string kind, string header, string sent, string answer)
    {
        await using var provider = Provider(kind, new RecordingHandler().Answer(HttpStatusCode.OK, answer), out var handler, allowTenantKeys: "true");

        var adapter = provider.GetRequiredService<ModelAdapters>().WithKey("main", TenantKey);
        await adapter.CallAsync(Call(), CancellationToken);

        Assert.Equal(sent, Assert.Single(handler.Requests).Headers[header]);
    }

    [Fact]
    public async Task A_tenants_key_for_a_missing_binding_fails_and_the_message_holds_no_key()
    {
        await using var provider = Provider("anthropic", new RecordingHandler(), out _, allowTenantKeys: "true");

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ModelAdapters>().WithKey("other", TenantKey));

        Assert.Contains("Ai:Bindings", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TenantKey, exception.ToString(), StringComparison.Ordinal);
    }

    private ServiceProvider Provider(string kind, RecordingHandler recording, out RecordingHandler handler, string allowTenantKeys)
    {
        handler = recording;
        var keyFile = Path.Combine(_folder, "key");
        File.WriteAllText(keyFile, InstallationKey + "\n");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxisAgent(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = kind,
            ["Ai:Bindings:main:Endpoint"] = "http://models.example",
            ["Ai:Bindings:main:Model"] = "model-1",
            ["Ai:Bindings:main:KeyFile"] = keyFile,
            ["Ai:DefaultBinding"] = "main",
            ["Ai:AllowTenantKeys"] = allowTenantKeys,
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=unused",
        }).Build(), new TestEnvironment());
        services.AddHttpClient(ModelAdapters.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => recording);
        return services.BuildServiceProvider();
    }
}
