namespace Axis.Core.Tenancy;

/// <summary>Holds the tenant of the current asynchronous flow, or <see langword="null"/> when none is resolved.</summary>
public interface ITenantContextAccessor
{
    TenantContext? Current { get; set; }
}
