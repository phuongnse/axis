using Axis.Configuration.Packages;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Processes;

public static class ProcessesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the work item runner, the handler that runs process steps, the handler that resumes a
    /// parent when its sub-process child completes, the handler that expires
    /// tasks at their deadline, the handler that sends
    /// connector calls with the cache of their <c>oauth2</c> access tokens, the handler that runs package operation calls, the handler that delivers
    /// raised events to their triggers, the handler that runs the occurrences of schedule triggers,
    /// the handler that starts their processes for the records of their data sources, and the handler that
    /// backfills the computed values of an entity that activation left pending, and the handler that runs the
    /// dry run of an uploaded record import, the handler that commits a checked import, and the handler that
    /// sends the emails that <c>notify</c> steps staged, with the host's mail settings. The runner
    /// runs the registered <see cref="IWorkItemHandler"/> and <see cref="IExternalWorkItemHandler"/>
    /// services and needs the tenancy services.
    /// </summary>
    /// <param name="allowHttpConnectorUrls">
    /// Whether a connector call, or a package operation through a <c>url</c> binding, may go to an
    /// <c>http</c> URL. Pass <see langword="true"/> only in Development.
    /// </param>
    /// <param name="allowPlainSmtp">
    /// Whether the worker may send email with <c>UseTls</c> off. Pass <see langword="true"/> only in Development.
    /// </param>
    /// <param name="deliveryAttempts">The failures of a delivery that make it a dead letter. At least 1.</param>
    /// <param name="statementTimeout">The <c>statement_timeout</c> of every statement a work item runs. At least one millisecond.</param>
    /// <param name="importMaxRows">The most data rows an import file may hold before its dry run fails. At least 1.</param>
    /// <param name="email">The host's mail settings. A tenant's own settings replace them whole.</param>
    public static IServiceCollection AddProcesses(
        this IServiceCollection services,
        bool allowHttpConnectorUrls,
        bool allowPlainSmtp,
        int deliveryAttempts,
        TimeSpan statementTimeout,
        int importMaxRows,
        EmailDefaults email)
    {
        ArgumentNullException.ThrowIfNull(email);

        services.AddSingleton<WorkItemRunner>();
        services.AddSingleton<ReleaseModelCache>();
        services.AddSingleton<IWorkItemHandler, ProcessStepHandler>();
        services.AddSingleton<IWorkItemHandler, ProcessTimerHandler>();
        services.AddSingleton<IWorkItemHandler, ProcessResumeHandler>();
        services.AddSingleton<IWorkItemHandler, TaskEscalationHandler>();
        services.AddSingleton<IWorkItemHandler, TaskDeadlineHandler>();
        services.AddSingleton(new ConnectorCallSettings(allowHttpConnectorUrls));
        services.AddSingleton(new DeliverySettings(deliveryAttempts));
        services.AddSingleton(new WorkItemSettings(statementTimeout));
        services.AddSingleton<ConnectorClient>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectorTokens, ConnectorTokenCache>();
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
        services.AddSingleton(email);
        services.AddSingleton(new EmailSecuritySettings(allowPlainSmtp));
        services.AddSingleton<IExternalWorkItemHandler, NotificationEmailHandler>();
        services.AddSingleton<IExternalWorkItemHandler, WebhookDeliveryHandler>();
        return services;
    }

    /// <summary>
    /// Makes the handler that runs package operation calls send each attempt of a C# package, unless it is
    /// <c>inProcess</c>, and each attempt of a <c>node</c> or <c>python</c> package, always, to a runner of the package host that
    /// <paramref name="isolation"/> names for its runtime, see
    /// docs/reference/extension-packages.md#package-runners. It also sends each attempt of an application's own package to a
    /// runner of the application package host at <c>Extensions:Runners:Application</c>, see
    /// docs/reference/extension-packages.md#an-applications-packages, so it needs the <see cref="ApplicationPackageSettings"/> that the
    /// caller registered. Call it after <see cref="AddProcesses"/>. The runners are closed when the host stops.
    /// </summary>
    public static IServiceCollection AddPackageRunners(this IServiceCollection services, PackageIsolation isolation)
    {
        ArgumentNullException.ThrowIfNull(isolation);

        // A factory, so that the container disposes the runners when the host stops.
        services.AddSingleton(provider => new PackageRunnerPools(
            isolation,
            provider.GetRequiredService<ApplicationPackageSettings>(),
            provider.GetRequiredService<ConnectorCallSettings>().AllowHttp));
        return services;
    }
}
