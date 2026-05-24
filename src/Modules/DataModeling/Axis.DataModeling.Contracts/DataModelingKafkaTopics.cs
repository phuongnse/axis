namespace Axis.DataModeling.Contracts;

/// <summary>Kafka topic names for DataModeling cross-module events (ADR-019, ADR-025).</summary>
public static class DataModelingKafkaTopics
{
    public const string ModelCreated = "axis.datamodeling.model-created";
    public const string ModelDeleted = "axis.datamodeling.model-deleted";
    public const string DataClassCreated = "axis.datamodeling.data-class-created";
    public const string DataClassDeleted = "axis.datamodeling.data-class-deleted";
    public const string DataRecordCreated = "axis.datamodeling.data-record-created";
    public const string DataRecordDeleted = "axis.datamodeling.data-record-deleted";
    public const string FieldAdded = "axis.datamodeling.field-added";
    public const string FieldUpdated = "axis.datamodeling.field-updated";
    public const string FieldRemoved = "axis.datamodeling.field-removed";
}
