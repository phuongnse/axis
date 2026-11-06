using Axis.Hosting;

namespace Axis.Agent;

public static class AgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the model bindings and the health checks that <see cref="HostHealthEndpoints.MapHostHealth"/> serves, and the
    /// background service of the agent host. The host reads <c>Ai:Bindings:&lt;name&gt;</c> with its <c>Kind</c>, <c>Endpoint</c>,
    /// <c>Model</c>, <c>KeyFile</c> and <c>MaxOutputTokens</c>, which defaults to 16000, and <c>Ai:DefaultBinding</c> and
    /// <c>Ai:ReviewBinding</c>. With no <c>Ai:DefaultBinding</c> it starts idle, because AI authoring is off. It has no health
    /// check yet, so both endpoints answer Healthy while it runs. <c>Health:Urls</c> defaults to <c>http://127.0.0.1:5209</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Health:Urls</c> is blank, a binding has an unknown <c>Kind</c>, no <c>Model</c>, a <c>KeyFile</c> that cannot be read or a
    /// <c>MaxOutputTokens</c> that is not a whole number of at least 1, or <c>Ai:DefaultBinding</c> or <c>Ai:ReviewBinding</c> names no binding.
    /// The message names the setting and never its value.
    /// </exception>
    public static IServiceCollection AddAxisAgent(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Read before anything starts, so a bad value stops the start.
        var health = HealthOptions.Load(configuration, "http://127.0.0.1:5209");
        var settings = AiSettings.Load(configuration);

        services.AddSingleton(health);
        services.AddSingleton(settings);
        // Both endpoints answer Healthy while the process runs, until the request loop has something to report on.
        services.AddHealthChecks();
        services.AddHostedService<AgentService>();
        return services;
    }
}
