using Axis.Processes;
using Axis.Tenancy;

namespace Axis.Worker;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the tenancy and processes services and the background service that runs the due
    /// work items of every configured tenant. Only a worker that runs in Development sends connector
    /// calls to an <c>http</c> base URL.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant or worker configuration is invalid.</exception>
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

        services.AddTenancy(configuration, environment);
        services.AddProcesses(environment.IsDevelopment());
        services.AddSingleton(options);
        services.AddHostedService<WorkerService>();
        return services;
    }
}
