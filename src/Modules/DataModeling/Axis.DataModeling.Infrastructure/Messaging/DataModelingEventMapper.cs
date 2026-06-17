using axis.datamodeling.events;
using Axis.DataModeling.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Infrastructure.Messaging;

/// <summary>Maps DataModeling domain events to Avro contract messages for Kafka (ADR-019).</summary>
internal static class DataModelingEventMapper
{
    public static object? ToIntegrationEvent(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            ModelCreated created => new ModelCreatedEvent
            {
                modelId = created.ModelId.ToString(),
                workspaceId = created.workspaceId.ToString(),
                name = created.Name,
            },
            ModelDeleted deleted => new ModelDeletedEvent
            {
                modelId = deleted.ModelId.ToString(),
                workspaceId = deleted.workspaceId.ToString(),
            },
            DataClassCreated dataClassCreated => new DataClassCreatedEvent
            {
                dataClassId = dataClassCreated.DataClassId.ToString(),
                workspaceId = dataClassCreated.workspaceId.ToString(),
                name = dataClassCreated.Name,
            },
            DataClassDeleted dataClassDeleted => new DataClassDeletedEvent
            {
                dataClassId = dataClassDeleted.DataClassId.ToString(),
                workspaceId = dataClassDeleted.workspaceId.ToString(),
            },
            DataRecordCreated recordCreated => new DataRecordCreatedEvent
            {
                recordId = recordCreated.RecordId.ToString(),
                modelId = recordCreated.ModelId.ToString(),
                workspaceId = recordCreated.workspaceId.ToString(),
            },
            DataRecordDeleted recordDeleted => new DataRecordDeletedEvent
            {
                recordId = recordDeleted.RecordId.ToString(),
                modelId = recordDeleted.ModelId.ToString(),
                workspaceId = recordDeleted.workspaceId.ToString(),
            },
            FieldAdded fieldAdded => new FieldAddedEvent
            {
                modelId = fieldAdded.ModelId.ToString(),
                workspaceId = fieldAdded.workspaceId.ToString(),
                fieldId = fieldAdded.FieldId.ToString(),
                fieldName = fieldAdded.FieldName,
                fieldType = fieldAdded.FieldType.ToString(),
                label = fieldAdded.Label,
                isRequired = fieldAdded.IsRequired,
                displayOrder = fieldAdded.DisplayOrder,
            },
            FieldUpdated fieldUpdated => new FieldUpdatedEvent
            {
                modelId = fieldUpdated.ModelId.ToString(),
                workspaceId = fieldUpdated.workspaceId.ToString(),
                fieldId = fieldUpdated.FieldId.ToString(),
                fieldName = fieldUpdated.FieldName,
                fieldType = fieldUpdated.FieldType.ToString(),
                label = fieldUpdated.Label,
                isRequired = fieldUpdated.IsRequired,
            },
            FieldRemoved fieldRemoved => new FieldRemovedEvent
            {
                modelId = fieldRemoved.ModelId.ToString(),
                workspaceId = fieldRemoved.workspaceId.ToString(),
                fieldId = fieldRemoved.FieldId.ToString(),
                fieldName = fieldRemoved.FieldName,
            },
            _ => null,
        };
}
