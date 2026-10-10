namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.audit_records</c>: one consequential action. Rows are only ever inserted, and
/// a database trigger rejects every change or removal.
/// </summary>
public sealed class AuditRecordRow
{
    /// <summary>A version 7 UUID.</summary>
    public required Guid Id { get; init; }

    /// <summary>The time of the transaction that wrote the action.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>A user id, <c>system</c> for the worker, or <c>anonymous</c> when nobody is signed in.</summary>
    public required string Actor { get; init; }

    /// <summary>The action, such as <c>record.created</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The <c>id</c> of the application manifest.</summary>
    public required Guid ApplicationId { get; init; }

    /// <summary>The <c>id</c> of the entity, when the action concerns a record.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>The id of the record.</summary>
    public Guid? RecordId { get; init; }

    /// <summary>The id of the process instance.</summary>
    public Guid? ProcessInstanceId { get; init; }

    /// <summary>A JSON object that depends on the action.</summary>
    public required string Details { get; init; }
}
