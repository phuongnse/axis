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
}
