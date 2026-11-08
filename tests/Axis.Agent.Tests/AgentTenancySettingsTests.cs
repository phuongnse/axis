using Axis.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Agent.Tests;

public sealed class AgentTenancySettingsTests
{
    private const string Password = "pw-from-the-platform-connection";

    private static Dictionary<string, string?> AiOn() => new()
    {
        ["Ai:Bindings:main:Kind"] = "anthropic",
        ["Ai:Bindings:main:Model"] = "model-1",
        ["Ai:DefaultBinding"] = "main",
    };

    private static ServiceCollection Add(Dictionary<string, string?> values, string environment = "Testing")
    {
        var services = new ServiceCollection();
        services.AddAxisAgent(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), new TestEnvironment(environment));
        return services;
    }

    [Fact]
    public void With_ai_authoring_on_a_missing_platform_connection_under_directory_stops_the_start_and_names_the_setting()
    {
        var values = AiOn();
        values["Tenancy:Source"] = "directory";

        var exception = Assert.Throws<InvalidOperationException>(() => Add(values));

        Assert.Contains("ConnectionStrings:Platform", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void With_ai_authoring_on_a_weak_tls_mode_outside_development_stops_the_start_and_holds_no_password()
    {
        var values = AiOn();
        values["Tenancy:Source"] = "directory";
        values["ConnectionStrings:Platform"] = $"Host=db.example.test;Database=axis;Username=axis;Password={Password};SSL Mode=Disable";

        var exception = Assert.Throws<InvalidOperationException>(() => Add(values, "Production"));

        Assert.Contains("ConnectionStrings:Platform", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_ai_authoring_on_no_tenant_under_configuration_stops_the_start()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Add(AiOn()));

        Assert.Contains("No tenants are configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_ai_authoring_off_the_tenancy_settings_and_secrets_are_not_read()
    {
        var services = Add(new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" });
        await using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ITenantCatalog>());
        Assert.Null(provider.GetService<BackgroundTenantCatalog>());
        Assert.Null(provider.GetService<AuthoringRequestStore>());
    }
}
