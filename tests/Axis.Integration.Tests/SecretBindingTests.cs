using Axis.Configuration.Tests;
using Axis.Core.Tenancy;
using Axis.Tenancy;
using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Integration.Tests;

public sealed class SecretBindingTests
{
    private const string Value = "s3cr3t-4f9b-x7q2";

    [Fact]
    public void Outside_development_a_secret_set_in_a_json_file_fails_worker_startup_naming_the_binding_and_no_value()
    {
        using var folder = new TemporaryFolder().With("appsettings.json", SettingsFile(Value));
        var builder = CreateBuilder("Production", Path.Combine(folder.Path, "appsettings.json"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Tenant 'a' binding 'PurchasingSystemToken' is a secret set in the file", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Tenants__a__Bindings__PurchasingSystemToken__Value", exception.Message, StringComparison.Ordinal);
        AssertHidesValue(exception.Message);
    }

    [Fact]
    public void Outside_development_the_same_secret_from_an_environment_variable_starts_the_worker_and_resolves()
    {
        using var folder = new TemporaryFolder().With("appsettings.json", SettingsFile("<from environment>"));
        var prefix = $"AXIS_TEST_{Guid.NewGuid():N}_";
        var variable = $"{prefix}Tenants__a__Bindings__PurchasingSystemToken__Value";
        Environment.SetEnvironmentVariable(variable, Value);
        try
        {
            var builder = CreateBuilder("Production", Path.Combine(folder.Path, "appsettings.json"));
            builder.Configuration.AddEnvironmentVariables(prefix);
            builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
            using var host = builder.Build();

            var accessor = host.Services.GetRequiredService<ITenantContextAccessor>();
            accessor.Current = new TenantContext("a");
            try
            {
                Assert.Equal(Value, host.Services.GetRequiredService<IDeploymentBindings>().ResolveSecret("PurchasingSystemToken").Reveal());
            }
            finally
            {
                accessor.Current = null;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void A_worker_whose_tenants_have_only_a_connection_string_starts()
    {
        // The worker reads no SchemaConnectionString. Only the server does.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<IDeploymentBindings>());
    }

    [Fact]
    public void In_development_a_secret_set_in_a_json_file_starts_the_worker()
    {
        using var folder = new TemporaryFolder().With("appsettings.json", SettingsFile(Value));
        var builder = CreateBuilder("Development", Path.Combine(folder.Path, "appsettings.json"));

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<IDeploymentBindings>());
    }

    private static HostApplicationBuilder CreateBuilder(string environmentName, string settingsFile)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environmentName, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });
        builder.Configuration.AddJsonFile(settingsFile);
        return builder;
    }

    private static string SettingsFile(string value) =>
        $$"""
        { "Tenants": { "a": { "Bindings": { "PurchasingSystemToken": { "Kind": "secret", "Value": "{{value}}" } } } } }
        """;

    // Neither the value nor its first or last four characters may show.
    private static void AssertHidesValue(string text)
    {
        Assert.DoesNotContain(Value, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Value[..4], text, StringComparison.Ordinal);
        Assert.DoesNotContain(Value[^4..], text, StringComparison.Ordinal);
    }
}
