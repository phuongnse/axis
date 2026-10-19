using Axis.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Integration.Tests;

/// <summary>The worker refuses a setting that cannot work, at startup and with the setting's name.</summary>
public sealed class WorkerOptionsTests
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    public void A_metrics_interval_that_is_not_positive_stops_the_worker_at_startup_naming_the_setting(string interval)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:MetricsInterval"] = interval,
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Worker:MetricsInterval", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_directory_without_a_secret_key_the_worker_stops_at_startup_naming_the_setting()
    {
        var builder = WorkerBuilder(source: "directory", secretKey: null);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_directory_with_a_valid_secret_key_the_worker_is_built()
    {
        var builder = WorkerBuilder(source: "directory", secretKey: Convert.ToBase64String(new byte[32]));

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
    }

    [Fact]
    public void Under_configuration_the_worker_needs_no_secret_key()
    {
        var builder = WorkerBuilder(source: "configuration", secretKey: null);

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
    }

    [Fact]
    public void An_unknown_tenancy_source_stops_the_worker_at_startup_naming_the_setting()
    {
        var builder = WorkerBuilder(source: "file", secretKey: null);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Tenancy:Source", exception.Message, StringComparison.Ordinal);
    }

    private static HostApplicationBuilder WorkerBuilder(string source, string? secretKey)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = source,
            ["Platform:SecretKey"] = secretKey,
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });
        return builder;
    }
}
