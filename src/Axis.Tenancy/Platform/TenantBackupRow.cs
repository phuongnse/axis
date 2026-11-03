namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_backups</c>: when the last backup of one tenant of a cell succeeded. <see cref="LastSuccessAt"/> is
/// the moment the archive was uploaded. A tenant that never succeeded has no row.
/// </summary>
public sealed class TenantBackupRow
{
    public string TenantId { get; set; } = "";

    public DateTimeOffset LastSuccessAt { get; set; }
}
