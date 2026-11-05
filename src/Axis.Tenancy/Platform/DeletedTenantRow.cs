namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.deleted_tenants</c>: the tombstone of one purged tenant. It holds the tenant id, when the purge removed its hosts, who asked
/// for the deletion and the hosts the tenant had, and nothing else. The table has no foreign key to the tenants, so a tombstone outlives its tenant.
/// </summary>
public sealed class DeletedTenantRow
{
    public string Id { get; set; } = "";

    public DateTimeOffset DeletedAt { get; set; }

    public string Actor { get; set; } = "";

    public string[] Hosts { get; set; } = [];
}
