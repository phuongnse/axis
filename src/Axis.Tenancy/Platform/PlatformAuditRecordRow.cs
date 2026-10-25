namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.audit_records</c>: one platform command that changed something. <see cref="Changed"/> holds
/// the names of what changed, never a secret value. The table is append-only.
/// </summary>
public sealed class PlatformAuditRecordRow
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The command that ran, such as <c>tenant binding set</c>.</summary>
    public string Command { get; set; } = "";

    /// <summary>The tenant the command acted on. Empty when it acts on no single tenant. It has no foreign key, so a record outlives its tenant.</summary>
    public string? TenantId { get; set; }

    public string OsUser { get; set; } = "";

    public string Machine { get; set; } = "";

    public string[] Changed { get; set; } = [];
}
