namespace Axis.Core.Tenancy;

/// <summary>The tenant that the current request or job runs for.</summary>
public sealed record TenantContext(string TenantId);
