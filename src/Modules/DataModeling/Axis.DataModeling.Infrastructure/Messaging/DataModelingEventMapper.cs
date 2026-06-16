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
                tenantId = created.tenantId.ToString(),
                name = created.Name,
            },
            ModelDeleted deleted => new ModelDeletedEvent
            {
                modelId = deleted.ModelId.ToString(),
                tenantId = deleted.tenantId.ToString(),
            },
            DataClassCreated dataClassCreated => new DataClassCreatedEvent
            {
                dataClassId = dataClassCreated.DataClassId.ToString(),
                tenantId = dataClassCreated.tenantId.ToString(),
                name = dataClassCreated.Name,
            },
            DataClassDeleted dataClassDeleted => new DataClassDeletedEvent
            {
                dataClassId = dataClassDeleted.DataClassId.ToString(),
                tenantId = dataClassDeleted.tenantId.ToString(),
            },
            DataRecordCreated recordCreated => new DataRecordCreatedEvent
            {
                recordId = recordCreated.RecordId.ToString(),
                modelId = recordCreated.ModelId.ToString(),
                tenantId = recordCreated.tenantId.ToString(),
            },
            DataRecordDeleted recordDeleted => new DataRecordDeletedEvent
            {
                recordId = recordDeleted.RecordId.ToString(),
                modelId = recordDeleted.ModelId.ToString(),
                tenantId = recordDeleted.tenantId.ToString(),
            },
            FieldAdded fieldAdded => new FieldAddedEvent
            {
                modelId = fieldAdded.ModelId.ToString(),
                tenantId = fieldAdded.tenantId.ToString(),
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
                tenantId = fieldUpdated.tenantId.ToString(),
                fieldId = fieldUpdated.FieldId.ToString(),
                fieldName = fieldUpdated.FieldName,
                fieldType = fieldUpdated.FieldType.ToString(),
                label = fieldUpdated.Label,
                isRequired = fieldUpdated.IsRequired,
            },
            FieldRemoved fieldRemoved => new FieldRemovedEvent
            {
                modelId = fieldRemoved.ModelId.ToString(),
                tenantId = fieldRemoved.tenantId.ToString(),
                fieldId = fieldRemoved.FieldId.ToString(),
                fieldName = fieldRemoved.FieldName,
            },
            _ => null,
        };
}
