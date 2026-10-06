using Axis.Core.Tenancy;

namespace Axis.Tenancy;

/// <summary>Keeps the tenant per asynchronous flow, so concurrent requests and jobs never see each other's tenant.</summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> _currentTenant = new();

    public TenantContext? Current
    {
        get => _currentTenant.Value;
        set => _currentTenant.Value = value;
    }
}
