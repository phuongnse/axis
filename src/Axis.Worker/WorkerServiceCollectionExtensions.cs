using System.Globalization;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Core.Telemetry;
using Axis.Processes;
using Axis.Processes.Instances;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenancy and processes services, the installed extension packages and the
    /// background service that runs the due work items of every tenant, and the health checks
    /// that <see cref="WorkerHealthEndpoints.MapWorkerHealth"/> serves. Under <c>Tenancy:Source=directory</c> the
    /// tenants are read from the tenant directory over <c>ConnectionStrings:Platform</c> once the worker runs, and again every
    /// <c>Tenancy:RefreshInterval</c>. The dry run of an import fails a file with more data rows than
    /// <c>Data:ImportMaxRows</c>, which defaults to 10000. The worker deletes in-app notifications older than
    /// <c>Notifications:RetentionDays</c>, which defaults to 90. Only a worker
    /// that runs in Development sends connector calls to an <c>http</c> base URL. The worker sends the emails that
    /// <c>notify</c> steps staged over the SMTP server of the <c>Email</c> section, which a tenant's own <c>Email</c> block
    /// replaces whole. With no <c>Email:Smtp:Host</c> it sends none, and only a worker that runs in Development sends email
    /// with <c>Email:Smtp:UseTls</c> off. Only a worker that runs in Development exports telemetry to an endpoint that is not <c>https</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant, worker or health configuration is invalid, <c>Data:ImportMaxRows</c> or <c>Notifications:RetentionDays</c> is not an integer of at least 1, <c>Tenancy:Source</c> is unknown, <c>ConnectionStrings:Platform</c>, <c>Platform:SecretKey</c> or <c>Tenancy:RefreshInterval</c> is missing or invalid under
    /// <c>directory</c>, <c>ConnectionStrings:Platform</c> sets an SSL mode below <c>VerifyFull</c> or none under <c>directory</c> outside Development and Testing, <c>Extensions:Path</c> or <c>Modules:Path</c> names a folder that does not exist, the <c>Email</c> section is invalid, or outside Development an OTLP endpoint variable is set and is not an absolute <c>https</c> URL.
    /// </exception>
    public static IServiceCollection AddAxisWorker(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new WorkerOptions();
        configuration.GetSection(WorkerOptions.SectionName).Bind(options);
        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.LeaseDuration)}' must be positive.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.PollInterval)}' must be positive.");
        }

        if (options.MetricsInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.MetricsInterval)}' must be positive.");
        }

        if (options.DeliveryAttempts < 1)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.DeliveryAttempts)}' must be at least 1.");
        }

        var health = new HealthOptions();
        configuration.GetSection(HealthOptions.SectionName).Bind(health);
        if (string.IsNullOrWhiteSpace(health.Urls))
        {
            throw new InvalidOperationException($"'{HealthOptions.SectionName}:{nameof(HealthOptions.Urls)}' must not be empty.");
        }

        WorkerTenantCatalog catalog;
        ITenantKeySource keySource;
        if (TenancySettings.Source(configuration) == TenancySource.Directory)
        {
            var platformConnectionString = configuration.GetConnectionString("Platform");
            if (string.IsNullOrWhiteSpace(platformConnectionString))
            {
                throw new InvalidOperationException("ConnectionStrings:Platform is required when Tenancy:Source is 'directory'.");
            }

            DatabaseTlsRequirement.Require(environment, "ConnectionStrings:Platform", platformConnectionString);

            // Read first, so a bad value stops the start even when the database is not there.
            var refreshInterval = TenancySettings.RefreshInterval(configuration);

            var cipher = TenantSecretCipher.Load(configuration);
            services.AddSingleton(cipher);

            // The worker reads the directory when it runs, retries until a read works, and reads it again every
            // refresh interval. It ignores a Tenants section.
            catalog = WorkerTenantCatalog.FromDirectory(
                platformConnectionString,
                cipher,
                refreshInterval,
                configuration.GetSection(TenantOptions.SectionName).Exists());
            keySource = new DirectoryTenantKeySource(platformConnectionString, cipher);
        }
        else
        {
            catalog = WorkerTenantCatalog.Loaded(ConfigurationTenantCatalog.Load(configuration, environment));
            keySource = new ConfigurationTenantKeySource(configuration);
        }

        services.AddWorkerTelemetry(configuration, environment);
        services.AddTenancy(catalog);
        services.AddTenantKeys(keySource);
        services.AddSingleton(catalog);
        services.AddExtensionPackages(configuration, environment);
        services.AddModules(configuration, environment);
        services.AddProcesses(environment.IsDevelopment(), environment.IsDevelopment(), options.DeliveryAttempts, ImportMaxRows(configuration), EmailDefaults.Load(configuration));
        services.AddSingleton(options);
        services.AddSingleton(new NotificationOptions { RetentionDays = RetentionDays(configuration) });
        services.AddSingleton(health);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<WorkerHealth>();
        services.AddHealthChecks()
            .AddCheck<WorkerLoopHealthCheck>("loop", tags: [WorkerHealthEndpoints.LiveTag, WorkerHealthEndpoints.ReadyTag])
            .AddCheck<TenantDirectoryHealthCheck>("directory", tags: [WorkerHealthEndpoints.ReadyTag])
            .AddCheck<TenantMigrationsHealthCheck>("migrations", tags: [WorkerHealthEndpoints.ReadyTag]);
        services.AddSingleton<StartupCheckState>();
        services.AddSingleton<AxisGauges>();
        services.AddSingleton<WorkerGauges>();
        services.AddHostedService<WorkerService>();
        services.AddHostedService<WorkerMetricsService>();
        return services;
    }

    private static int ImportMaxRows(IConfiguration configuration)
    {
        const string setting = "Data:ImportMaxRows";
        if (configuration[setting] is not { Length: > 0 } text)
        {
            return 10000;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var rows) && rows >= 1
            ? rows
            : throw new InvalidOperationException($"'{setting}' must be an integer of at least 1.");
    }

    private static int RetentionDays(IConfiguration configuration)
    {
        const string setting = "Notifications:RetentionDays";
        if (configuration[setting] is not { Length: > 0 } text)
        {
            return 90;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days >= 1
            ? days
            : throw new InvalidOperationException($"'{setting}' must be an integer of at least 1.");
    }
}
