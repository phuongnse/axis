namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.restore_checks</c>: the result of one run of <c>cell restore-check</c> after its pick. It holds when the run started, the source
/// tenant and whether the check passed, and nothing else. The table has no foreign key to the tenants, so a row outlives its tenant.
/// </summary>
public sealed class RestoreCheckRow
{
    public Guid Id { get; set; }

    public DateTimeOffset RanAt { get; set; }

    public string TenantId { get; set; } = "";

    public bool Passed { get; set; }
}
