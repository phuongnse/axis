using Axis.Agent.Models;
using Axis.Hosting;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Agent;

public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the model bindings, the adapters that call them and the health checks that <see cref="HostHealthEndpoints.MapHostHealth"/> serves, and the
    /// background service of the agent host. The host reads <c>Ai:Bindings:&lt;name&gt;</c> with its <c>Kind</c>, <c>Endpoint</c>,
    /// <c>Model</c>, <c>KeyFile</c> and <c>MaxOutputTokens</c>, which defaults to 16000, and <c>Ai:DefaultBinding</c> and
    /// <c>Ai:ReviewBinding</c>. With no <c>Ai:DefaultBinding</c> it starts idle, because AI authoring is off: it reads no tenant setting
    /// and no secret. With one, it reads its tenants as the worker does, see <c>docs/reference/host-settings.md#ai-settings</c>: from the
    /// <c>Tenants</c> section, or under <c>Tenancy:Source=directory</c> from the tenant directory over <c>ConnectionStrings:Platform</c>,
    /// once the host runs and again every <c>Tenancy:RefreshInterval</c>. It has no health
    /// check yet, so both endpoints answer Healthy while it runs. <c>Health:Urls</c> defaults to <c>http://127.0.0.1:5209</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Health:Urls</c> is blank, a binding has an unknown <c>Kind</c>, no <c>Model</c>, a <c>KeyFile</c> that cannot be read or a
    /// <c>MaxOutputTokens</c> that is not a whole number of at least 1, or <c>Ai:DefaultBinding</c> or <c>Ai:ReviewBinding</c> names no binding.
    /// With <c>Ai:DefaultBinding</c> set, also <c>Tenancy:Source</c> is unknown, no tenant is configured under <c>configuration</c>,
    /// <c>ConnectionStrings:Platform</c>, <c>Platform:SecretKey</c> or <c>Tenancy:RefreshInterval</c> is missing or invalid under
    /// <c>directory</c>, or <c>ConnectionStrings:Platform</c> sets an SSL mode below <c>VerifyFull</c> or none outside Development and Testing.
    /// The message names the setting and never a connection value.
    /// </exception>
    public static IServiceCollection AddAxisAgent(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Read before anything starts, so a bad value stops the start.
        var health = HealthOptions.Load(configuration, "http://127.0.0.1:5209");
        var settings = AiSettings.Load(configuration);

        var catalog = settings.DefaultBinding is null ? null : LoadTenantCatalog(configuration, environment);

        services.AddSingleton(health);
        services.AddSingleton(settings);
        if (catalog is not null)
        {
            services.AddTenancy(catalog);
            services.AddSingleton(catalog);
            services.AddSingleton<AuthoringRequestStore>();
        }

        services.TryAddSingleton(TimeProvider.System);
        // A model call follows no redirect and never logs the key. It can take minutes, because the adapters do not stream.
        services.AddHttpClient(ModelAdapters.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RedactLoggedHeaders(["x-api-key", "Authorization"]);
        services.AddSingleton<ModelAdapters>();
        // Both endpoints answer Healthy while the process runs, until the request loop has something to report on.
        services.AddHealthChecks();
        services.AddHostedService<AgentService>();
        return services;
    }

    // The agent host reads its tenants as the worker does. Under directory it reads them when it runs, retries until a read works,
    // and reads the directory again every refresh interval. It ignores a Tenants section and holds no key source.
    private static BackgroundTenantCatalog LoadTenantCatalog(IConfiguration configuration, IHostEnvironment environment)
    {
        if (TenancySettings.Source(configuration) != TenancySource.Directory)
        {
            return BackgroundTenantCatalog.Loaded(ConfigurationTenantCatalog.Load(configuration, environment));
        }

        var platformConnectionString = configuration.GetConnectionString("Platform");
        if (string.IsNullOrWhiteSpace(platformConnectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Platform is required when Tenancy:Source is 'directory'.");
        }

        DatabaseTlsRequirement.Require(environment, "ConnectionStrings:Platform", platformConnectionString);

        // Read first, so a bad value stops the start even when the database is not there.
        var refreshInterval = TenancySettings.RefreshInterval(configuration);
        var cipher = TenantSecretCipher.Load(configuration);
        return BackgroundTenantCatalog.FromDirectory(
            platformConnectionString,
            cipher,
            refreshInterval,
            configuration.GetSection(TenantOptions.SectionName).Exists());
    }

    /// <summary>Makes every binding return the scripted answers in order, for tests and the journey.</summary>
    /// <exception cref="InvalidOperationException">The environment is not Development or Testing.</exception>
    public static IServiceCollection AddScriptedModelAdapter(this IServiceCollection services, IHostEnvironment environment, IEnumerable<ModelAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(answers);

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException("The scripted model adapter is refused outside Development and Testing.");
        }

        return services.AddSingleton(new ScriptedModelAdapter(answers));
    }
}
