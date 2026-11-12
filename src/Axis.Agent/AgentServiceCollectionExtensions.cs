using Axis.Agent.Models;
using Axis.Data.Records;
using Axis.Hosting;
using Axis.Tenancy;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Axis.Agent;

public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the model bindings, the adapters that call them and the health checks that <see cref="HostHealthEndpoints.MapHostHealth"/> serves, and the
    /// background service of the agent host. The host reads <c>Ai:Bindings:&lt;name&gt;</c> with its <c>Kind</c>, <c>Endpoint</c>,
    /// <c>Model</c>, <c>KeyFile</c> and <c>MaxOutputTokens</c>, which defaults to 16000, and <c>Ai:DefaultBinding</c>,
    /// <c>Ai:ReviewBinding</c> and <c>Ai:FixRounds</c>, which defaults to 3. With no <c>Ai:DefaultBinding</c> it starts idle, because AI authoring is off: it reads no tenant setting
    /// and no secret. With one, it reads <c>Ai:AllowTenantKeys</c>, its tenants and their data keys as the worker does, see <c>docs/reference/host-settings.md#ai-settings</c>: from the
    /// <c>Tenants</c> section, or under <c>Tenancy:Source=directory</c> from the tenant directory over <c>ConnectionStrings:Platform</c>,
    /// once the host runs and again every <c>Tenancy:RefreshInterval</c>. With them it gives a step the author's access token through
    /// <see cref="AuthoringStepContext.AccessTokenAsync"/>, refreshed at the tenant's realm as the agent client, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>, and a step calls the tenant's MCP server as the author through <see cref="AuthoringStepContext.Mcp"/>. It also runs every registered <see cref="IAuthoringStep"/> on the
    /// requests it claims with a lease, one step of one request per tenant turn, see <c>docs/reference/authoring-requests.md#durable-requests</c>, and claims only a state that has a step.
    /// With AI authoring on it registers <see cref="PlanningStep"/>, which writes the plan of a request in <c>planning</c>, and <see cref="WorkingStep"/>, which makes the approved change in the request's draft in <c>working</c>, and <see cref="CheckingStep"/>, which compiles the draft and runs every scenario on it in <c>checking</c>, and <see cref="ReviewingStep"/>, which reviews the checked draft in a new model session in <c>reviewing</c>, and <see cref="FixingStep"/>, which works on the stored draft again in <c>fixing</c>. <see cref="TenantModelAdapters"/> gives the steps the adapter of a binding for a tenant, and under <c>directory</c> the adapter adds the tokens of each call to the tenant's usage over <c>ConnectionStrings:Platform</c>, see <c>docs/reference/tenant-quotas.md#usage</c>. It has no health check yet, so both endpoints answer Healthy while it runs. <c>Health:Urls</c> defaults to <c>http://127.0.0.1:5209</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Health:Urls</c> is blank, a binding has an unknown <c>Kind</c>, no <c>Model</c>, a <c>KeyFile</c> that cannot be read or a
    /// <c>MaxOutputTokens</c> that is not a whole number of at least 1, <c>Ai:DefaultBinding</c> or <c>Ai:ReviewBinding</c> names no binding, or <c>Ai:FixRounds</c> is not a whole number of at least 0.
    /// With <c>Ai:DefaultBinding</c> set, also <c>Tenancy:Source</c> is unknown, no tenant is configured under <c>configuration</c>,
    /// <c>Ai:AllowTenantKeys</c> is not <c>true</c> or <c>false</c>,
    /// <c>ConnectionStrings:Platform</c>, <c>Platform:SecretKey</c> or <c>Tenancy:RefreshInterval</c> is missing or invalid under
    /// <c>directory</c>, or <c>ConnectionStrings:Platform</c> sets an SSL mode below <c>VerifyFull</c> or none outside Development and Testing.
    /// Also two registered <see cref="IAuthoringStep"/> work on one state, or one works on a state that needs no work.
    /// The message names the setting and never a connection value.
    /// </exception>
    public static IServiceCollection AddAxisAgent(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // Read before anything starts, so a bad value stops the start.
        var health = HealthOptions.Load(configuration, "http://127.0.0.1:5209");
        var settings = AiSettings.Load(configuration);

        var tenantKeys = settings.DefaultBinding is null ? null : TenantModelKeySettings.Load(configuration);
        (BackgroundTenantCatalog Catalog, DirectoryTenantModelKeys? ModelKeys, TenantUsageStore? Usage, ITenantKeySource KeySource)? tenants =
            settings.DefaultBinding is null ? null : LoadTenantCatalog(configuration, environment);

        services.AddSingleton(health);
        services.AddSingleton(settings);
        if (tenants is var (catalog, modelKeys, usage, keySource))
        {
            services.AddTenancy(catalog);
            services.AddSingleton(catalog);
            services.AddSingleton(tenantKeys!);
            if (modelKeys is not null)
            {
                services.AddSingleton(modelKeys);
            }

            if (usage is not null)
            {
                services.AddSingleton(usage);
            }

            // The data keys decrypt the stored refresh tokens of the requests.
            services.AddTenantKeys(keySource);
            services.AddSingleton<SensitiveValues>();
            services.AddSingleton<TenantModelAdapters>();
            services.AddSingleton<AuthoringRequestStore>();
            services.TryAddSingleton(AuthoringLoopOptions.Default);
            services.AddSingleton<IAuthoringStep, PlanningStep>();
            services.AddSingleton<IAuthoringStep, WorkingStep>();
            services.TryAddSingleton(CheckingOptions.Default);
            services.AddSingleton<IAuthoringStep, CheckingStep>();
            services.AddSingleton<IAuthoringStep, ReviewingStep>();
            services.AddSingleton<IAuthoringStep, FixingStep>();
            services.AddSingleton<AuthoringSteps>();
            services.AddSingleton<AuthoringRequestTokenRefresher>();
            services.AddSingleton<AuthoringRequestRunner>();
            // A realm call follows no redirect and never logs a body.
            services.AddHttpClient(AuthoringRequestTokenRefresher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
            // A call to the tenant's MCP server follows no redirect and never logs the token or the session id.
            services.AddHttpClient(AuthorMcpClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(60))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
                .RedactLoggedHeaders(["Authorization", "Mcp-Session-Id"]);
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
    // and reads the directory again every refresh interval. It ignores a Tenants section. Its key source reads the data keys from the
    // Tenants section, or under directory from the tenant directory as the server does. Under directory it also
    // reads a tenant's own model key from the same database, see docs/reference/model-bindings.md#a-tenants-own-key, and writes the tokens of
    // its model calls to the tenant's usage there, see docs/reference/tenant-quotas.md#usage.
    private static (BackgroundTenantCatalog Catalog, DirectoryTenantModelKeys? ModelKeys, TenantUsageStore? Usage, ITenantKeySource KeySource) LoadTenantCatalog(IConfiguration configuration, IHostEnvironment environment)
    {
        if (TenancySettings.Source(configuration) != TenancySource.Directory)
        {
            return (BackgroundTenantCatalog.Loaded(ConfigurationTenantCatalog.Load(configuration, environment)), null, null, new ConfigurationTenantKeySource(configuration));
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
        var catalog = BackgroundTenantCatalog.FromDirectory(
            platformConnectionString,
            cipher,
            refreshInterval,
            configuration.GetSection(TenantOptions.SectionName).Exists());
        return (catalog, new DirectoryTenantModelKeys(platformConnectionString, cipher), new TenantUsageStore(NpgsqlDataSource.Create(platformConnectionString)), new DirectoryTenantKeySource(platformConnectionString, cipher));
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
