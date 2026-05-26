namespace Axis.Identity.Contracts;

/// <summary>Scheduled hard-delete after the 30-day grace period (US-007).</summary>
public sealed record OrganizationHardDeleteJob(Guid OrganizationId);
