using axis.workflowengine.events;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowEngine.Domain.Events;

namespace Axis.WorkflowEngine.Infrastructure.Messaging;

/// <summary>Maps WorkflowEngine domain events to Avro contract messages for Kafka (ADR-019).</summary>
internal static class WorkflowEngineEventMapper
{
    public static object? ToIntegrationEvent(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            FormStepReached reached => new FormStepReachedEvent
            {
                executionId = reached.ExecutionId.ToString(),
                executionStepId = reached.ExecutionStepId.ToString(),
                workflowDefinitionId = reached.WorkflowDefinitionId.ToString(),
                tenantId = reached.tenantId.ToString(),
                formDefinitionId = reached.FormDefinitionId.ToString(),
                assigneeExpression = reached.AssigneeExpression,
                timeoutHours = reached.TimeoutHours,
            },
            _ => null,
        };
}
