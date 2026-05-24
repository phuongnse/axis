using axis.workflowengine.events;

namespace Axis.WorkflowEngine.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class WorkflowEngineEventExtensions
{
    public static Guid ExecutionId(this FormStepReachedEvent @event)
        => ParseRequiredGuid(@event.executionId, nameof(@event.executionId));

    public static Guid ExecutionStepId(this FormStepReachedEvent @event)
        => ParseRequiredGuid(@event.executionStepId, nameof(@event.executionStepId));

    public static Guid WorkflowDefinitionId(this FormStepReachedEvent @event)
        => ParseRequiredGuid(@event.workflowDefinitionId, nameof(@event.workflowDefinitionId));

    public static Guid OrganizationId(this FormStepReachedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid FormDefinitionId(this FormStepReachedEvent @event)
        => ParseRequiredGuid(@event.formDefinitionId, nameof(@event.formDefinitionId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
