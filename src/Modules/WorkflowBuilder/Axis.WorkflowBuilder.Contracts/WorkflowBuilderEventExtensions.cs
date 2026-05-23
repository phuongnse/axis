using axis.workflowbuilder.events;

namespace Axis.WorkflowBuilder.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class WorkflowBuilderEventExtensions
{
    public static Guid WorkflowId(this WorkflowPublishedEvent @event) => Guid.Parse(@event.workflowId);

    public static Guid OrganizationId(this WorkflowPublishedEvent @event) => Guid.Parse(@event.organizationId);

    public static IReadOnlyList<Guid> ReferencedFormIds(this WorkflowPublishedEvent @event)
        => @event.referencedFormIds.Select(Guid.Parse).ToList();

    public static Guid WorkflowId(this WorkflowArchivedEvent @event) => Guid.Parse(@event.workflowId);

    public static Guid OrganizationId(this WorkflowArchivedEvent @event) => Guid.Parse(@event.organizationId);

    public static Guid WorkflowId(this WorkflowUnarchivedEvent @event) => Guid.Parse(@event.workflowId);

    public static Guid OrganizationId(this WorkflowUnarchivedEvent @event) => Guid.Parse(@event.organizationId);

    public static IReadOnlyList<Guid> ReferencedFormIds(this WorkflowUnarchivedEvent @event)
        => @event.referencedFormIds.Select(Guid.Parse).ToList();
}
