using axis.datamodeling.events;

namespace Axis.DataModeling.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class DataModelingEventExtensions
{
    public static Guid ModelId(this ModelCreatedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this ModelCreatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid ModelId(this ModelDeletedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this ModelDeletedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid DataClassId(this DataClassCreatedEvent @event)
        => ParseRequiredGuid(@event.dataClassId, nameof(@event.dataClassId));

    public static Guid OrganizationId(this DataClassCreatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid DataClassId(this DataClassDeletedEvent @event)
        => ParseRequiredGuid(@event.dataClassId, nameof(@event.dataClassId));

    public static Guid OrganizationId(this DataClassDeletedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid RecordId(this DataRecordCreatedEvent @event)
        => ParseRequiredGuid(@event.recordId, nameof(@event.recordId));

    public static Guid ModelId(this DataRecordCreatedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this DataRecordCreatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid RecordId(this DataRecordDeletedEvent @event)
        => ParseRequiredGuid(@event.recordId, nameof(@event.recordId));

    public static Guid ModelId(this DataRecordDeletedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this DataRecordDeletedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid ModelId(this FieldAddedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this FieldAddedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid FieldId(this FieldAddedEvent @event)
        => ParseRequiredGuid(@event.fieldId, nameof(@event.fieldId));

    public static Guid ModelId(this FieldUpdatedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this FieldUpdatedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid FieldId(this FieldUpdatedEvent @event)
        => ParseRequiredGuid(@event.fieldId, nameof(@event.fieldId));

    public static Guid ModelId(this FieldRemovedEvent @event)
        => ParseRequiredGuid(@event.modelId, nameof(@event.modelId));

    public static Guid OrganizationId(this FieldRemovedEvent @event)
        => ParseRequiredGuid(@event.organizationId, nameof(@event.organizationId));

    public static Guid FieldId(this FieldRemovedEvent @event)
        => ParseRequiredGuid(@event.fieldId, nameof(@event.fieldId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
