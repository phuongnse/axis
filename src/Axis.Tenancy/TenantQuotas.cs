namespace Axis.Tenancy;

/// <summary>
/// The effective quotas of a tenant, see <c>docs/reference/tenant-quotas.md#quotas</c>. In a cell a tenant's own row of
/// <c>platform.tenant_quotas</c> wins and a quota with no row takes the cell's default. A <c>null</c> value means no limit.
/// The rows come with the tenant catalog, so a changed row takes effect at the next directory read. Outside a cell every
/// quota is no limit.
/// </summary>
public sealed class TenantQuotas(CellOptions cell, QuotaDefaults defaults, ITenantCatalog catalog)
{
    /// <summary>The limit of <paramref name="quota"/> for the tenant, or <c>null</c> for no limit.</summary>
    /// <exception cref="ArgumentException"><paramref name="quota"/> is not one of <see cref="QuotaKeys.All"/>.</exception>
    public long? Limit(string tenantId, string quota)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(quota);

        if (!QuotaKeys.All.Contains(quota, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{quota}' is not a quota.", nameof(quota));
        }

        if (!cell.IsCell)
        {
            return null;
        }

        return catalog.Find(tenantId)?.Quotas is { } own && own.TryGetValue(quota, out var value) ? value : defaults[quota];
    }

    /// <summary>The limit of every quota for the tenant, in the order of <see cref="QuotaKeys.All"/>.</summary>
    public IReadOnlyDictionary<string, long?> For(string tenantId) =>
        QuotaKeys.All.ToDictionary(quota => quota, quota => Limit(tenantId, quota), StringComparer.Ordinal);
}
