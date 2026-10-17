using Axis.Core.Telemetry;
using Axis.Processes.Work;

namespace Axis.Processes.Instances;

/// <summary>Counts the process instances that end, by the names of their release.</summary>
internal static class InstanceMetrics
{
    /// <summary>
    /// Adds one to <c>axis.process.instances.ended</c> for an instance that ended in <paramref name="state"/>.
    /// The names come from the instance's release. When the release cannot be loaded, the end is still
    /// counted, without them, so counting never fails the transaction that ends the instance.
    /// </summary>
    internal static async Task RecordEndedAsync(
        ReleaseModelCache models,
        WorkItemContext context,
        LoadedInstance instance,
        string state,
        CancellationToken cancellationToken)
    {
        string? application = null;
        string? process = null;
        try
        {
            var model = await models.GetAsync(context, instance.ReleaseId, cancellationToken);
            application = model.Manifest.Name;
            process = model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId)?.Name;
        }
        catch (InvalidOperationException)
        {
            // The release is gone or does not compile, so the end is counted without names.
        }

        AxisMetrics.RecordInstanceEnded(context.Item.TenantId, application, process, state);
    }
}
