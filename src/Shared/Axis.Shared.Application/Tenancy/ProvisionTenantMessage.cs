namespace Axis.Shared.Application.Tenancy;

/// <summary>
/// Enqueued after email verification is persisted (US-003). Processed by the API host provisioner.
/// </summary>
public sealed record ProvisionTenantMessage(Guid OrganizationId);
