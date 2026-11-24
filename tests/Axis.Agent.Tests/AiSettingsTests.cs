using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Agent;
using Axis.Hosting;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
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
        { "unreadable-host-key-file", "Ai:HostKeyFile" },
        { "p384-host-key-file", "Ai:HostKeyFile" },
        { "public-host-key-file", "Ai:HostKeyFile" },
        { "default-names-no-binding", "Ai:DefaultBinding" },
        { "review-names-no-binding", "Ai:ReviewBinding" },
        { "max-output-tokens-zero", "Ai:Bindings:main:MaxOutputTokens" },
        { "max-output-tokens-text", "Ai:Bindings:main:MaxOutputTokens" },
        { "max-output-tokens-fraction", "Ai:Bindings:main:MaxOutputTokens" },
        { "fix-rounds-negative", "Ai:FixRounds" },
        { "fix-rounds-text", "Ai:FixRounds" },
        { "request-token-budget-zero", "Ai:RequestTokenBudget" },
        { "request-token-budget-text", "Ai:RequestTokenBudget" },
        { "request-timeout-zero", "Ai:RequestTimeout" },
        { "request-timeout-text", "Ai:RequestTimeout" },
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
            case "unreadable-host-key-file":
                values["Ai:HostKeyFile"] = missingFile;
                break;
            case "p384-host-key-file":
                values["Ai:HostKeyFile"] = HostKeyFile(ECCurve.NamedCurves.nistP384);
                break;
            case "public-host-key-file":
                values["Ai:HostKeyFile"] = HostKeyFile(ECCurve.NamedCurves.nistP256, publicOnly: true);
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
            case "fix-rounds-negative":
                values["Ai:FixRounds"] = "-4242";
                break;
            case "fix-rounds-text":
                values["Ai:FixRounds"] = "three-rounds";
                break;
            case "request-token-budget-zero":
                values["Ai:RequestTokenBudget"] = "0";
                break;
            case "request-token-budget-text":
                values["Ai:RequestTokenBudget"] = "lots-of-tokens";
                break;
            case "request-timeout-zero":
                values["Ai:RequestTimeout"] = "00:00:00";
                break;
            case "request-timeout-text":
                values["Ai:RequestTimeout"] = "half-an-hour-value";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var exception = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment()));

        var text = exception.ToString();
        Assert.Contains($"'{setting}'", text, StringComparison.Ordinal);
        foreach (var secret in new[] { BadKind, ModelId, keyFile, missingFile, _folder, "host-key.pem", Key, "other-binding-name", "lots-of-tokens", "70707.5", "half-an-hour-value", "-4242", "three-rounds" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_p_256_host_key_file_is_loaded_with_its_key_id_and_public_key_and_only_with_ai_authoring_on()
    {
        var file = HostKeyFile(ECCurve.NamedCurves.nistP256);
        using var expected = ECDiffieHellman.Create();
        expected.ImportFromPem(File.ReadAllText(file));
        var parameters = expected.ExportParameters(false);

        var settings = AiSettings.Load(Configure(new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = ModelId,
            ["Ai:DefaultBinding"] = "main",
            ["Ai:HostKeyFile"] = file,
        }));
        using var key = AgentHostKey.Load(settings.HostKeyFile!, null);

        Assert.Equal(JwkThumbprint.Of(parameters).KeyId, key.KeyId);
        Assert.Equal([0x04, .. parameters.Q.X!, .. parameters.Q.Y!], key.PublicKey);
    }

    [Fact]
    public void With_a_key_service_the_host_key_file_holds_the_wrapped_key_and_a_value_sealed_to_the_public_key_opens()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyService = KeyServiceOf(keyFileBase64);
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var file = Path.Combine(_folder, "host-key.pem");
        File.WriteAllText(file, keyService.Wrap(KeyService.AgentHostKey, pair.ExportPkcs8PrivateKeyPem()) + "\n");

        using var key = AgentHostKey.Load(file, keyService);

        Assert.DoesNotContain("-----BEGIN", File.ReadAllText(file), StringComparison.Ordinal);
        byte[] info = "axis test info"u8.ToArray();
        byte[] aad = "axis test aad"u8.ToArray();
        var sealedValue = Hpke.Seal(pair.ExportParameters(false), info, aad, "sk-person-1234"u8);
        Assert.Equal("sk-person-1234"u8.ToArray(), key.Open(info, aad, sealedValue));
        Assert.Equal(JwkThumbprint.Of(pair.ExportParameters(false)).KeyId, key.KeyId);
    }

    [Theory]
    [InlineData("installation-key")]
    [InlineData("another-key-file")]
    [InlineData("plain-pem")]
    public void With_a_key_service_a_host_key_that_does_not_unwrap_is_refused_with_the_setting_and_no_path_or_pem(string case_)
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyService = KeyServiceOf(keyFileBase64);
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pem = pair.ExportPkcs8PrivateKeyPem();
        var value = case_ switch
        {
            "installation-key" => keyService.Wrap(KeyService.InstallationKey, pem),
            "another-key-file" => KeyServiceOf(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), "other-key-file").Wrap(KeyService.AgentHostKey, pem),
            _ => pem,
        };
        var file = Path.Combine(_folder, "host-key.pem");
        File.WriteAllText(file, value);

        var exception = Assert.Throws<InvalidOperationException>(() => AgentHostKey.Load(file, keyService));

        Assert.Equal("The setting Ai:HostKeyFile does not unwrap with the key of KeyService:KeyFile.", exception.Message);
        var text = exception.ToString();
        foreach (var hidden in new[] { "-----BEGIN", file, _folder, "host-key.pem", keyFileBase64, value })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void With_a_key_service_set_in_the_settings_AddAxisAgent_unwraps_the_host_key_file()
    {
        var keyFileBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var keyService = KeyServiceOf(keyFileBase64);
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var file = Path.Combine(_folder, "host-key.pem");
        File.WriteAllText(file, keyService.Wrap(KeyService.AgentHostKey, pair.ExportPkcs8PrivateKeyPem()));
        var values = new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = ModelId,
            ["Ai:DefaultBinding"] = "main",
            ["Ai:HostKeyFile"] = file,
            ["KeyService:Kind"] = "file",
            ["KeyService:KeyFile"] = Path.Combine(_folder, "key-service-key"),
        };

        // The host key loads before the tenants, so a wrapped key that unwraps gets as far as the missing tenants.
        var unwrapped = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment()));
        Assert.Contains("No tenants are configured", unwrapped.Message, StringComparison.Ordinal);
        values["Ai:HostKeyFile"] = Path.Combine(_folder, "plain-host-key.pem");
        File.WriteAllText(values["Ai:HostKeyFile"]!, pair.ExportPkcs8PrivateKeyPem());
        var plain = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment()));
        Assert.Equal("The setting Ai:HostKeyFile does not unwrap with the key of KeyService:KeyFile.", plain.Message);
    }

    [Fact]
    public void Outside_development_and_testing_a_host_key_file_without_a_key_service_stops_the_start_and_development_accepts_it()
    {
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var file = Path.Combine(_folder, "plain-host-key.pem");
        File.WriteAllText(file, pair.ExportPkcs8PrivateKeyPem());
        var values = new Dictionary<string, string?>
        {
            ["Ai:Bindings:main:Kind"] = "anthropic",
            ["Ai:Bindings:main:Model"] = ModelId,
            ["Ai:DefaultBinding"] = "main",
            ["Ai:HostKeyFile"] = file,
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=unused",
        };

        var refused = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment("Production")));
        var services = new ServiceCollection().AddAxisAgent(Configure(values), new TestEnvironment("Development"));

        Assert.Equal("The setting KeyService:Kind is required outside Development and Testing.", refused.Message);
        Assert.DoesNotContain(file, refused.ToString(), StringComparison.Ordinal);
        Assert.Contains(services, service => service.ServiceType == typeof(AgentHostKey));
    }

    private IKeyService KeyServiceOf(string keyFileBase64, string name = "key-service-key")
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, keyFileBase64 + "\n");
        return KeyService.Load(Configure(new Dictionary<string, string?> { ["KeyService:Kind"] = "file", ["KeyService:KeyFile"] = path }))!;
    }

    [Fact]
    public void With_ai_authoring_off_a_bad_host_key_file_is_ignored()
    {
        var services = new ServiceCollection();

        services.AddAxisAgent(Configure(new Dictionary<string, string?> { ["Ai:HostKeyFile"] = Path.Combine(_folder, "missing-key-file") }), new TestEnvironment());

        Assert.Null(AiSettings.Load(Configure(new Dictionary<string, string?> { ["Ai:HostKeyFile"] = "anything" })).HostKeyFile);
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(AgentHostKey));
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
    public void The_fix_rounds_are_3_when_unset_and_0_is_accepted()
    {
        Assert.Equal(3, AiSettings.Load(Configure(new Dictionary<string, string?>())).FixRounds);
        Assert.Equal(0, AiSettings.Load(Configure(new Dictionary<string, string?> { ["Ai:FixRounds"] = "0" })).FixRounds);
        Assert.Equal(5, AiSettings.Load(Configure(new Dictionary<string, string?> { ["Ai:FixRounds"] = "5" })).FixRounds);
    }

    [Fact]
    public void The_request_budgets_have_their_defaults_and_are_read_when_set()
    {
        var defaults = AiSettings.Load(Configure([]));
        Assert.Equal((2_000_000L, TimeSpan.FromMinutes(30)), (defaults.RequestTokenBudget, defaults.RequestTimeout));

        var set = AiSettings.Load(Configure(new Dictionary<string, string?>
        {
            ["Ai:RequestTokenBudget"] = "5000",
            ["Ai:RequestTimeout"] = "00:10:00",
        }));
        Assert.Equal((5000L, TimeSpan.FromMinutes(10)), (set.RequestTokenBudget, set.RequestTimeout));
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

        Assert.Equal(AiSettings.SettingNames.Append(TenantModelKeySettings.AllowSetting).Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    private string KeyFile()
    {
        var path = Path.Combine(_folder, "key");
        File.WriteAllText(path, Key + "\n");
        return path;
    }

    private string HostKeyFile(ECCurve curve, bool publicOnly = false)
    {
        var path = Path.Combine(_folder, "host-key.pem");
        using var key = ECDiffieHellman.Create(curve);
        File.WriteAllText(path, publicOnly ? key.ExportSubjectPublicKeyInfoPem() : key.ExportPkcs8PrivateKeyPem());
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
