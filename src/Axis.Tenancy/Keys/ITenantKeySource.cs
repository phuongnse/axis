namespace Axis.Tenancy.Keys;

/// <summary>Reads all the keys of one tenant from where the tenancy source keeps them.</summary>
public interface ITenantKeySource
{
    /// <summary>Returns every data key and lookup key of the tenant, in no order. A tenant with no keys gives an empty list.</summary>
    /// <exception cref="InvalidOperationException">
    /// A stored or configured key is not valid. The message names the tenant and the field or setting, never a value.
    /// </exception>
    Task<IReadOnlyList<TenantKey>> ReadAsync(string tenantId, CancellationToken cancellationToken);
}
