using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Processes;

public static class ProcessesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the work item runner and the handler that runs process steps. The runner runs the
    /// registered <see cref="IWorkItemHandler"/> services and needs the tenancy services.
    /// </summary>
    public static IServiceCollection AddProcesses(this IServiceCollection services)
    {
        services.AddSingleton<WorkItemRunner>();
        services.AddSingleton<ReleaseModelCache>();
        services.AddSingleton<IWorkItemHandler, ProcessStepHandler>();
        return services;
    }
}
