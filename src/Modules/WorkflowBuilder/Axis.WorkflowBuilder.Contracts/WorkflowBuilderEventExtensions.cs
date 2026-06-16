using axis.workflowbuilder.events;

namespace Axis.WorkflowBuilder.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class WorkflowBuilderEventExtensions
{
    public static Guid WorkflowId(this WorkflowPublishedEvent @event)
        => ParseRequiredGuid(@event.workflowId, nameof(@event.workflowId));

    public static Guid TeamAccountId(this WorkflowPublishedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static IReadOnlyList<Guid> ReferencedFormIds(this WorkflowPublishedEvent @event)
        => ParseReferencedFormIds(@event.referencedFormIds);

    public static Guid WorkflowId(this WorkflowArchivedEvent @event)
        => ParseRequiredGuid(@event.workflowId, nameof(@event.workflowId));

    public static Guid TeamAccountId(this WorkflowArchivedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid WorkflowId(this WorkflowUnarchivedEvent @event)
        => ParseRequiredGuid(@event.workflowId, nameof(@event.workflowId));

    public static Guid TeamAccountId(this WorkflowUnarchivedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static IReadOnlyList<Guid> ReferencedFormIds(this WorkflowUnarchivedEvent @event)
        => ParseReferencedFormIds(@event.referencedFormIds);

    private static IReadOnlyList<Guid> ParseReferencedFormIds(IList<string> referencedFormIds)
    {
        List<Guid> parsed = new(referencedFormIds.Count);
        for (int index = 0; index < referencedFormIds.Count; index++)
        {
            string value = referencedFormIds[index];
            parsed.Add(ParseRequiredGuid(value, $"referencedFormIds[{index}]"));
        }

        return parsed;
    }

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
