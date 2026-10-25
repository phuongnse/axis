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

    public const string ProcessRetried = "process.retried";

    public const string TaskCreated = "task.created";

    public const string TaskCompleted = "task.completed";

    public const string TaskClaimed = "task.claimed";

    public const string TaskReleased = "task.released";

    public const string TaskExpired = "task.expired";

    public const string ConnectorCallStaged = "connector.callStaged";

    public const string ConnectorCalled = "connector.called";

    public const string ConnectorCallResolved = "connector.callResolved";

    public const string OperationStaged = "operation.staged";

    public const string OperationCalled = "operation.called";

    public const string OperationCallResolved = "operation.callResolved";

    public const string SessionSignedIn = "session.signedIn";

    public const string SessionSignedOut = "session.signedOut";

    public const string SessionSignInRefused = "session.signInRefused";
}
