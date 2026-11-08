using System.Net;
using System.Text.Json;
using Axis.Agent;
using Axis.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Agent.Tests;

public sealed class AiSettingsTests : IDisposable
{
    private const string Key = "sk-test-secret-1";
    private const string BadKind = "bad-kind-value";
    private const string ModelId = "model-id-value";

    private readonly string _folder = Directory.CreateTempSubdirectory("axis-agent-tests-").FullName;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    public static TheoryData<string, string> BadSettings => new()
    {
        { "unknown-kind", "Ai:Bindings:main:Kind" },
        { "missing-kind", "Ai:Bindings:main:Kind" },
        { "missing-model", "Ai:Bindings:main:Model" },
        { "unreadable-key-file", "Ai:Bindings:main:KeyFile" },
        { "default-names-no-binding", "Ai:DefaultBinding" },
        { "review-names-no-binding", "Ai:ReviewBinding" },
        { "max-output-tokens-zero", "Ai:Bindings:main:MaxOutputTokens" },
        { "max-output-tokens-text", "Ai:Bindings:main:MaxOutputTokens" },
        { "max-output-tokens-fraction", "Ai:Bindings:main:MaxOutputTokens" },
    };

    [Theory]
    [MemberData(nameof(BadSettings))]
    public void A_bad_setting_stops_the_start_and_the_message_names_the_setting_and_no_value(string scenario, string setting)
    {
        var keyFile = KeyFile();
        var missingFile = Path.Combine(_folder, "missing-key-file");
        var values = new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = ModelId,
            ["Ai:Bindings:main:KeyFile"] = keyFile,
            ["Ai:DefaultBinding"] = "main",
        };
        switch (scenario)
        {
            case "unknown-kind":
                values["Ai:Bindings:main:Kind"] = BadKind;
                break;
            case "missing-kind":
                values.Remove("Ai:Bindings:main:Kind");
                break;
            case "missing-model":
                values.Remove("Ai:Bindings:main:Model");
                break;
            case "unreadable-key-file":
                values["Ai:Bindings:main:KeyFile"] = missingFile;
                break;
            case "default-names-no-binding":
                values["Ai:DefaultBinding"] = "other-binding-name";
                break;
            case "review-names-no-binding":
                values["Ai:ReviewBinding"] = "other-binding-name";
                break;
            case "max-output-tokens-zero":
                values["Ai:Bindings:main:MaxOutputTokens"] = "0";
                break;
            case "max-output-tokens-text":
                values["Ai:Bindings:main:MaxOutputTokens"] = "lots-of-tokens";
                break;
            case "max-output-tokens-fraction":
                values["Ai:Bindings:main:MaxOutputTokens"] = "70707.5";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment()));

        var text = exception.ToString();
        Assert.Contains($"'{setting}'", text, StringComparison.Ordinal);
        foreach (var secret in new[] { BadKind, ModelId, keyFile, missingFile, _folder, Key, "other-binding-name", "lots-of-tokens", "70707.5" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_valid_binding_is_read_with_its_key_and_the_default_output_limit()
    {
        var settings = AiSettings.Load(Configure(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = ModelId,
            ["Ai:Bindings:main:KeyFile"] = KeyFile(),
            ["Ai:Bindings:local:Kind"] = "openai-compatible",
            ["Ai:Bindings:local:Endpoint"] = "http://localhost:11434/v1",
            ["Ai:Bindings:local:Model"] = "llama",
            ["Ai:Bindings:local:MaxOutputTokens"] = "4000",
            ["Ai:DefaultBinding"] = "main",
            ["Ai:ReviewBinding"] = "local",
        }));

        var main = settings.Bindings["main"];
        Assert.Equal((ModelBindingKind.Anthropic, ModelId, Key, 16000), (main.Kind, main.Model, main.Key, main.MaxOutputTokens));
        var local = settings.Bindings["local"];
        Assert.Equal((ModelBindingKind.OpenAiCompatible, "http://localhost:11434/v1", (string?)null, 4000), (local.Kind, local.Endpoint, local.Key, local.MaxOutputTokens));
        Assert.Equal(("main", "local"), (settings.DefaultBinding, settings.ReviewBinding));
        Assert.DoesNotContain(Key, main.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_default_binding_both_health_endpoints_answer_healthy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Health:Urls"] = "http://127.0.0.1:0" });
        builder.Services.AddAxisAgent(builder.Configuration, builder.Environment);
        await using var app = builder.Build();
        app.MapHostHealth();
        await app.StartAsync(CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/live"));
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/ready"));
        using var other = await client.GetAsync("/health", CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public void A_blank_health_address_stops_the_start()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddAxisAgent(Configure(new Dictionary<string, string?> { ["Health:Urls"] = " " }), new TestEnvironment()));

        Assert.Equal("'Health:Urls' must not be empty.", exception.Message);
    }

    [Fact]
    public void The_settings_table_of_the_model_bindings_page_holds_the_settings_that_are_read()
    {
        var documented = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "model-bindings.md"))
            .Where(line => line.StartsWith("| `Ai:", StringComparison.Ordinal) && !line.Contains("_(planned for", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1].Trim().Trim('`'))
            .ToList();

        Assert.Equal(AiSettings.SettingNames.Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    private string KeyFile()
    {
        var path = Path.Combine(_folder, "key");
        File.WriteAllText(path, Key + "\n");
        return path;
    }

    private static IConfiguration Configure(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static async Task<(HttpStatusCode Status, string? Health)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        // The answer holds only the overall status.
        Assert.Equal(["status"], body.RootElement.EnumerateObject().Select(property => property.Name));
        return (response.StatusCode, body.RootElement.GetProperty("status").GetString());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
