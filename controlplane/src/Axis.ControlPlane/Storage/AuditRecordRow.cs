namespace Axis.ControlPlane.Storage;

/// <summary>
/// A row of <c>control_plane.audit_records</c>: one change and who made it. <see cref="ActorKind"/> is <c>account</c>,
/// <c>webhook</c>, <c>job</c> or <c>operator</c>. The table is append-only: the database rejects every update, delete and truncate.
/// </summary>
public sealed class AuditRecordRow
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string ActorKind { get; set; } = "";

    public string Actor { get; set; } = "";

    public string Action { get; set; } = "";

    public string Subject { get; set; } = "";
}
