using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Processes;

public static class ProcessesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the work item runner, the handler that runs process steps, the handler that resumes a
    /// parent when its sub-process child completes, the handler that expires
    /// tasks at their deadline, the handler that sends
    /// connector calls, the handler that runs package operation calls, the handler that delivers
    /// raised events to their triggers, the handler that runs the occurrences of schedule triggers,
    /// the handler that starts their processes for the records of their data sources, and the handler that
    /// backfills the computed values of an entity that activation left pending, and the handler that runs the
    /// dry run of an uploaded record import, and the handler that commits a checked import. The runner
    /// runs the registered <see cref="IWorkItemHandler"/> and <see cref="IExternalWorkItemHandler"/>
    /// services and needs the tenancy services.
    /// </summary>
    /// <param name="allowHttpConnectorUrls">
    /// Whether a connector call, or a package operation through a <c>url</c> binding, may go to an
    /// <c>http</c> URL. Pass <see langword="true"/> only in Development.
    /// </param>
    /// <param name="deliveryAttempts">The failures of a delivery that make it a dead letter. At least 1.</param>
    /// <param name="importMaxRows">The most data rows an import file may hold before its dry run fails. At least 1.</param>
    public static IServiceCollection AddProcesses(this IServiceCollection services, bool allowHttpConnectorUrls, int deliveryAttempts, int importMaxRows)
    {
        services.AddSingleton<WorkItemRunner>();
        services.AddSingleton<ReleaseModelCache>();
        services.AddSingleton<IWorkItemHandler, ProcessStepHandler>();
        services.AddSingleton<IWorkItemHandler, ProcessTimerHandler>();
        services.AddSingleton<IWorkItemHandler, ProcessResumeHandler>();
        services.AddSingleton<IWorkItemHandler, TaskEscalationHandler>();
        services.AddSingleton<IWorkItemHandler, TaskDeadlineHandler>();
        services.AddSingleton(new ConnectorCallSettings(allowHttpConnectorUrls));
        services.AddSingleton(new DeliverySettings(deliveryAttempts));
        services.AddSingleton<ConnectorClient>();
        services.AddSingleton<PackageHttpTransport>();
        services.AddSingleton<IExternalWorkItemHandler, ConnectorCallHandler>();
        services.AddSingleton<IExternalWorkItemHandler, PackageCallHandler>();
        services.AddSingleton<IExternalWorkItemHandler, EventDeliveryHandler>();
        services.AddSingleton<IExternalWorkItemHandler, TriggerScheduleHandler>();
        services.AddSingleton<IExternalWorkItemHandler, TriggerFanOutHandler>();
        services.AddSingleton<IExternalWorkItemHandler, ComputedBackfillHandler>();
        services.AddSingleton(new RecordImportSettings(importMaxRows));
        services.AddSingleton<IExternalWorkItemHandler, RecordImportCheckHandler>();
        services.AddSingleton<IExternalWorkItemHandler, RecordImportCommitHandler>();
        return services;
    }
}
