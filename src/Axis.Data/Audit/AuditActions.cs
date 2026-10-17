namespace Axis.Data.Audit;

/// <summary>The actions an audit record names.</summary>
public static class AuditActions
{
    public const string RecordCreated = "record.created";

    public const string RecordUpdated = "record.updated";

    public const string RecordDeleted = "record.deleted";

    public const string ProcessStarted = "process.started";

    public const string ProcessStepCompleted = "process.stepCompleted";

    public const string ProcessCompleted = "process.completed";

    public const string ProcessFailed = "process.failed";

    public const string ProcessCancelled = "process.cancelled";

    public const string TaskCreated = "task.created";

    public const string TaskCompleted = "task.completed";

    public const string ConnectorCallStaged = "connector.callStaged";

    public const string ConnectorCalled = "connector.called";

    public const string OperationStaged = "operation.staged";

    public const string OperationCalled = "operation.called";
}
