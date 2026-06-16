namespace Axis.Identity.Contracts;

/// <summary>Scheduled hard-delete after the 30-day grace period.</summary>
public sealed record TenantHardDeleteJob(Guid tenantId);
