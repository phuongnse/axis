using Axis.Tenancy.Platform;

namespace Axis.Administration;

/// <summary>
/// Writes platform audit records to <c>platform.audit_records</c>. The command line and a later web console both call it,
/// so every platform command leaves the same kind of record.
/// </summary>
public static class PlatformAuditRecords
{
    /// <summary>
    /// Adds one record and saves <paramref name="platform"/>. The time, the operating system user and the machine come from
    /// the process, so a caller cannot misreport who ran the command. <paramref name="changed"/> holds the names of what
    /// changed and must never hold a secret value. <paramref name="tenantId"/> is <see langword="null"/> when the command
    /// acts on no single tenant.
    /// </summary>
    public static async Task WriteAsync(
        PlatformDbContext platform,
        string command,
        string? tenantId,
        IReadOnlyList<string> changed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(changed);

        platform.AuditRecords.Add(new PlatformAuditRecordRow
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = DateTimeOffset.UtcNow,
            Command = command,
            TenantId = tenantId,
            OsUser = Environment.UserName,
            Machine = Environment.MachineName,
            Changed = [.. changed],
        });
        await platform.SaveChangesAsync(cancellationToken);
    }
}
