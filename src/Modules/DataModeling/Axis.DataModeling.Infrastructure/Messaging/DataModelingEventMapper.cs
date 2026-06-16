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
                organizationId = created.OrganizationId.ToString(),
                name = created.Name,
            },
            ModelDeleted deleted => new ModelDeletedEvent
            {
                modelId = deleted.ModelId.ToString(),
                organizationId = deleted.OrganizationId.ToString(),
            },
            DataClassCreated dataClassCreated => new DataClassCreatedEvent
            {
                dataClassId = dataClassCreated.DataClassId.ToString(),
                organizationId = dataClassCreated.OrganizationId.ToString(),
                name = dataClassCreated.Name,
            },
            DataClassDeleted dataClassDeleted => new DataClassDeletedEvent
            {
                dataClassId = dataClassDeleted.DataClassId.ToString(),
                organizationId = dataClassDeleted.OrganizationId.ToString(),
            },
            DataRecordCreated recordCreated => new DataRecordCreatedEvent
            {
                recordId = recordCreated.RecordId.ToString(),
                modelId = recordCreated.ModelId.ToString(),
                organizationId = recordCreated.OrganizationId.ToString(),
            },
            DataRecordDeleted recordDeleted => new DataRecordDeletedEvent
            {
                recordId = recordDeleted.RecordId.ToString(),
                modelId = recordDeleted.ModelId.ToString(),
                organizationId = recordDeleted.OrganizationId.ToString(),
            },
            FieldAdded fieldAdded => new FieldAddedEvent
            {
                modelId = fieldAdded.ModelId.ToString(),
                organizationId = fieldAdded.OrganizationId.ToString(),
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
                organizationId = fieldUpdated.OrganizationId.ToString(),
                fieldId = fieldUpdated.FieldId.ToString(),
                fieldName = fieldUpdated.FieldName,
                fieldType = fieldUpdated.FieldType.ToString(),
                label = fieldUpdated.Label,
                isRequired = fieldUpdated.IsRequired,
            },
            FieldRemoved fieldRemoved => new FieldRemovedEvent
            {
                modelId = fieldRemoved.ModelId.ToString(),
                organizationId = fieldRemoved.OrganizationId.ToString(),
                fieldId = fieldRemoved.FieldId.ToString(),
                fieldName = fieldRemoved.FieldName,
            },
            _ => null,
        };
}
