using Axis.WorkflowBuilder.Domain.Aggregates;

namespace Axis.WorkflowBuilder.Application.Services;

public interface IWorkflowReferenceSync
{
    Task SyncAsync(WorkflowDefinition workflow, CancellationToken cancellationToken = default);
}
