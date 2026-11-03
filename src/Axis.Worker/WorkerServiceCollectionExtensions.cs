using System.Globalization;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Axis.Processes;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Axis.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenancy and processes services, the installed extension packages and the
    /// background service that runs the due work items of every tenant, and the health checks
    /// that <see cref="WorkerHealthEndpoints.MapWorkerHealth"/> serves. Under <c>Tenancy:Source=directory</c> the
    /// tenants are read from the tenant directory over <c>ConnectionStrings:Platform</c> once the worker runs, and again every
    /// <c>Tenancy:RefreshInterval</c>. Under <c>directory</c> the worker also holds one direct connection to the platform database, named <c>axis-worker-listen</c>,
    /// that runs <c>LISTEN axis_work</c>: a wake signal for a tenant runs its work at once, and the worker connects again when the connection drops. The dry run of an import fails a file with more data rows than
    /// <c>Data:ImportMaxRows</c>, which defaults to 10000. Every statement of a work item runs under <c>Worker:StatementTimeout</c>,
    /// which defaults to one minute. The tenant connection pools take their sizes from <c>Data:TenantPoolMaxSize</c>,
    /// <c>Data:TenantPoolMinSize</c> and <c>Data:TenantPoolIdleLifetime</c>. The worker deletes in-app notifications older than
    /// <c>Notifications:RetentionDays</c>, which defaults to 90. Only a worker
    /// that runs in Development sends connector calls to an <c>http</c> base URL. The worker sends the emails that
    /// <c>notify</c> steps staged over the SMTP server of the <c>Email</c> section, which a tenant's own <c>Email</c> block
    /// replaces whole. With no <c>Email:Smtp:Host</c> it sends none, and only a worker that runs in Development sends email
    /// with <c>Email:Smtp:UseTls</c> off. Only a worker that runs in Development exports telemetry to an endpoint that is not <c>https</c>.
    /// With <c>Cell:Id</c> set the worker is part of a cell and needs <c>Tenancy:Source=directory</c>, <c>Cell:Region</c> and <c>Cell:TenantDomain</c>.
    /// A worker in a cell also measures the storage of each tenant every hour and counts the work items it runs and the emails it sends for each tenant per UTC day, in the platform database over <c>ConnectionStrings:Platform</c>. An email past the tenant's <c>emailsPerDay</c> quota waits until 00:00 UTC. The dry run or commit of an import waits for a <c>concurrentImports</c> lease of the tenant, and is due again in one second when none is free.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant, worker, health or cell configuration is invalid, <c>Worker:StatementTimeout</c> is not a duration of at least one millisecond, <c>Worker:IdlePollCeiling</c> is below <c>Worker:PollInterval</c>, <c>Worker:Concurrency</c> or <c>Worker:Quantum</c> is below 1, <c>Data:ImportMaxRows</c> or <c>Notifications:RetentionDays</c> is not an integer of at least 1, <c>Data:TenantPoolMaxSize</c>, <c>Data:TenantPoolMinSize</c> or <c>Data:TenantPoolIdleLifetime</c> is invalid, <c>Tenancy:Source</c> is unknown, <c>ConnectionStrings:Platform</c>, <c>Platform:SecretKey</c> or <c>Tenancy:RefreshInterval</c> is missing or invalid under
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

        if (options.IdlePollCeiling < options.PollInterval)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.IdlePollCeiling)}' must be at least '{WorkerOptions.SectionName}:{nameof(WorkerOptions.PollInterval)}'.");
        }

        if (options.MetricsInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.MetricsInterval)}' must be positive.");
        }

        if (options.DeliveryAttempts < 1)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.DeliveryAttempts)}' must be at least 1.");
        }

        if (options.Concurrency < 1)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.Concurrency)}' must be at least 1.");
        }

        if (options.Quantum < 1)
        {
            throw new InvalidOperationException($"'{WorkerOptions.SectionName}:{nameof(WorkerOptions.Quantum)}' must be at least 1.");
        }

        var statementTimeout = StatementTimeout.Read(configuration, "Worker:StatementTimeout", WorkItemSettings.DefaultStatementTimeout);
        var pool = TenantPoolSettings.Load(configuration);

        var health = new HealthOptions();
        configuration.GetSection(HealthOptions.SectionName).Bind(health);
        if (string.IsNullOrWhiteSpace(health.Urls))
        {
            throw new InvalidOperationException($"'{HealthOptions.SectionName}:{nameof(HealthOptions.Urls)}' must not be empty.");
        }

        var cell = CellOptions.Load(configuration);
        services.AddSingleton(cell);

        // Read before any database work too, so a bad value stops the start.
        var runtimeAddress = TenantRuntimeAddress.Load(configuration, cell);
        services.AddSingleton(QuotaDefaults.Load(configuration, cell));
        services.AddSingleton<TenantQuotas>();

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
                configuration.GetSection(TenantOptions.SectionName).Exists(),
                runtimeAddress);
            keySource = new DirectoryTenantKeySource(platformConnectionString, cipher);

            // Listens for the wake signals of the server and the publisher, on one direct connection that does not use a pool.
            services.AddHostedService(provider => new WakeSignalListener(
                platformConnectionString,
                provider.GetRequiredService<WakeSignals>(),
                provider.GetRequiredService<WorkerOptions>(),
                provider.GetRequiredService<ILogger<WakeSignalListener>>()));

            // A cell always reads the directory, so its usage and its daily email count are written to the same platform database.
            if (cell.IsCell)
            {
                var platform = NpgsqlDataSource.Create(platformConnectionString);
                services.AddSingleton(new TenantUsageStore(platform));
                services.AddSingleton(new TenantDailyCounterStore(platform));
                services.AddSingleton<IEmailQuota, CellEmailQuota>();
                services.AddSingleton(provider => new TenantLeases(platform, provider.GetRequiredService<ILogger<TenantLeases>>()));
                services.AddSingleton<IImportLeases, CellImportLeases>();
                services.AddSingleton<ICallLeases, CellCallLeases>();
                services.AddSingleton<IWorkItemUsage, CellWorkItemUsage>();
            }
        }
        else
        {
            catalog = WorkerTenantCatalog.Loaded(ConfigurationTenantCatalog.Load(configuration, environment));
            keySource = new ConfigurationTenantKeySource(configuration);
        }

        services.AddWorkerTelemetry(configuration, environment);
        services.AddTenancy(catalog, pool);
        services.AddTenantKeys(keySource);
        services.AddSingleton<SensitiveValues>();
        services.AddSingleton(catalog);
        services.AddExtensionPackages(configuration, environment);
        services.AddModules(configuration, environment);
        services.AddProcesses(environment.IsDevelopment(), environment.IsDevelopment(), options.DeliveryAttempts, statementTimeout, ImportMaxRows(configuration), EmailDefaults.Load(configuration));
        services.AddSingleton(options);
        services.AddSingleton<WakeSignals>();
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
        services.AddMetrics();
        services.AddSingleton<WorkerPollMetrics>();
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
