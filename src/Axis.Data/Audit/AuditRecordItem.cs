namespace Axis.Data.Audit;

/// <summary>
/// A stored audit record of one record: its id, time, actor, action, process instance and
/// details. <see cref="Details"/> is the stored JSON object as text.
/// </summary>
public sealed record AuditRecordItem(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    Guid? ProcessInstanceId,
    string Details);

/// <summary>One page of a record's audit records and the number of its audit records.</summary>
public sealed record AuditRecordPage(IReadOnlyList<AuditRecordItem> Items, long TotalCount);
