using Axis.Processes.Instances;
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

    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")]
    public void An_http_otlp_endpoint_outside_Development_stops_the_worker_at_startup_naming_the_variable_and_not_its_value(string variable)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [variable] = "http://collector.example.test:4318",
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains(variable, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("collector.example.test", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", "http://collector.example.test:4318")]
    [InlineData("Production", "https://collector.example.test:4318")]
    public void An_http_otlp_endpoint_in_Development_and_an_https_one_elsewhere_build_the_worker(string environment, string endpoint)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = endpoint,
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
    }

    [Fact]
    public void A_delivery_attempts_setting_below_one_stops_the_worker_at_startup_naming_the_setting()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:DeliveryAttempts"] = "0",
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Worker:DeliveryAttempts", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void An_import_max_rows_setting_that_is_not_an_integer_of_at_least_one_stops_the_worker_at_startup_naming_the_setting(string value)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Data:ImportMaxRows"] = value,
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Data:ImportMaxRows", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void A_notification_retention_days_setting_that_is_not_an_integer_of_at_least_one_stops_the_worker_at_startup_naming_the_setting(string value)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:RetentionDays"] = value,
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Notifications:RetentionDays", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_directory_without_a_secret_key_the_worker_stops_at_startup_naming_the_setting()
    {
        var builder = WorkerBuilder(source: "directory", secretKey: null);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Platform:SecretKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_directory_without_a_platform_connection_the_worker_stops_at_startup_naming_the_setting()
    {
        var builder = WorkerBuilder(source: "directory", secretKey: Convert.ToBase64String(new byte[32]), platform: null);

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("ConnectionStrings:Platform", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void A_mail_host_without_a_sender_stops_the_worker_at_startup_naming_the_setting()
    {
        var builder = EmailBuilder(("Email:Smtp:Host", "smtp.example.test"));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains("Email:From", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Email:Smtp:Port", "0")]
    [InlineData("Email:Smtp:Port", "70000")]
    [InlineData("Email:Smtp:Port", "smtp-port-value")]
    [InlineData("Email:Smtp:UseTls", "maybe")]
    public void A_bad_mail_value_stops_the_worker_at_startup_naming_the_setting_and_never_quoting_the_value(string key, string value)
    {
        var builder = EmailBuilder(("Email:Smtp:Host", "smtp.example.test"), ("Email:From", "axis@example.test"), (key, value));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Services.AddAxisWorker(builder.Configuration, builder.Environment));

        Assert.Contains(key, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message.Replace(key, "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_mail_section_builds_the_worker_with_those_settings()
    {
        var builder = EmailBuilder(
            ("Email:Smtp:Host", "smtp.example.test"),
            ("Email:Smtp:Port", "2525"),
            ("Email:Smtp:UseTls", "false"),
            ("Email:From", "axis@example.test"));

        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);

        using var provider = builder.Services.BuildServiceProvider();
        var settings = provider.GetRequiredService<EmailDefaults>().Settings;
        Assert.NotNull(settings);
        Assert.Equal(("smtp.example.test", 2525, false, "axis@example.test"), (settings.Host, settings.Port, settings.UseTls, settings.From));
    }

    private static HostApplicationBuilder EmailBuilder(params (string Key, string? Value)[] settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });
        builder.Configuration.AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => setting.Value));
        return builder;
    }

    private static HostApplicationBuilder WorkerBuilder(
        string source,
        string? secretKey,
        string? platform = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis")
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Production", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = source,
            ["Platform:SecretKey"] = secretKey,
            ["ConnectionStrings:Platform"] = platform,
            ["Tenants:a:Hosts:0"] = "a.example.test",

            // No database is opened: the worker is built, never started.
            ["Tenants:a:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis",
        });
        return builder;
    }
}
