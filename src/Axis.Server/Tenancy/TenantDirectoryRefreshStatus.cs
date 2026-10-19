namespace Axis.Server.Tenancy;

/// <summary>
/// Whether the last read of the tenant directory failed. It stays set until a read works, and a failed read leaves the
/// last good directory in place.
/// </summary>
internal sealed class TenantDirectoryRefreshStatus
{
    private volatile bool _failing;

    public bool Failing => _failing;

    internal void Set(bool failing) => _failing = failing;
}
