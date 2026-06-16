using Axis.Shared.Domain.Primitives;

namespace Axis.WorkflowEngine.Domain.Events;

/// <summary>
/// Published by the WorkflowEngine when execution reaches a Form step.
/// FormBuilder listens to this event to create a FormSubmission task and notify the assignee.
/// </summary>
public sealed record FormStepReached(
    Guid ExecutionId,
    Guid ExecutionStepId,
    Guid WorkflowDefinitionId,
    Guid OrganizationId,
    Guid FormDefinitionId,
    string? AssigneeExpression,
    int? TimeoutHours) : IDomainEvent;
