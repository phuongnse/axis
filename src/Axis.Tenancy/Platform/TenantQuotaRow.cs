namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_quotas</c>: one quota that a tenant overrides. A quota with no row takes the cell's default.
/// </summary>
public sealed class TenantQuotaRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The quota's key, one of <see cref="QuotaKeys.All"/>, such as <c>users</c>.</summary>
    public string Quota { get; set; } = "";

    /// <summary>The tenant's limit, or <c>null</c> for no limit.</summary>
    public long? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
