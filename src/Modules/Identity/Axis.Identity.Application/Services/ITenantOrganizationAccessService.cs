namespace Axis.Identity.Application.Services;

/// <summary>
/// Evaluates whether an organization may use tenant-scoped module APIs (E01 F03 US-009).
/// </summary>
public interface ITenantOrganizationAccessService
{
    Task<TenantOrganizationAccessResult> EvaluateAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
