using System.Text.Json;
using axis.workflowbuilder.events;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Domain.Events;

namespace Axis.WorkflowBuilder.Infrastructure.Messaging;

/// <summary>Maps domain events to Avro contract messages for Kafka (ADR-019).</summary>
internal static class WorkflowBuilderEventMapper
{
    private static readonly JsonSerializerOptions ConfigJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static object? ToIntegrationEvent(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            WorkflowPublished published => MapPublished(published),
            WorkflowArchived archived => MapArchived(archived),
            WorkflowUnarchived unarchived => MapUnarchived(unarchived),
            _ => null,
        };

    private static WorkflowPublishedEvent MapPublished(WorkflowPublished published) =>
        new()
        {
            workflowId = published.WorkflowId.ToString(),
            teamAccountId = published.TeamAccountId.ToString(),
            referencedFormIds = published.ReferencedFormIds.Select(id => id.ToString()).ToList(),
            steps = published.Steps.Select(MapStep).ToList(),
            transitions = published.Transitions.Select(MapTransition).ToList(),
        };

    private static WorkflowArchivedEvent MapArchived(WorkflowArchived archived) =>
        new()
        {
            workflowId = archived.WorkflowId.ToString(),
            teamAccountId = archived.TeamAccountId.ToString(),
        };

    private static WorkflowUnarchivedEvent MapUnarchived(WorkflowUnarchived unarchived) =>
        new()
        {
            workflowId = unarchived.WorkflowId.ToString(),
            teamAccountId = unarchived.TeamAccountId.ToString(),
            referencedFormIds = unarchived.ReferencedFormIds.Select(id => id.ToString()).ToList(),
        };

    private static StepSnapshotRecord MapStep(StepSnapshot step) =>
        new()
        {
            id = step.Id.ToString(),
            name = step.Name,
            stepType = step.StepType,
            displayOrder = step.DisplayOrder,
            configJson = step.Config is null ? null : JsonSerializer.Serialize(step.Config, ConfigJsonOptions),
        };

    private static TransitionSnapshotRecord MapTransition(TransitionSnapshot transition) =>
        new()
        {
            fromStepId = transition.FromStepId.ToString(),
            toStepId = transition.ToStepId.ToString(),
            label = transition.Label,
        };
}
