using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Processes;

public static class ProcessesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the work item runner. It runs the registered <see cref="IWorkItemHandler"/> services
    /// and needs the tenancy services.
    /// </summary>
    public static IServiceCollection AddProcesses(this IServiceCollection services)
    {
        services.AddSingleton<WorkItemRunner>();
        return services;
    }
}
