namespace Axis.Identity.Application.Services;

/// <summary>Removes platform-level Identity records for a hard-deleted Tenant.</summary>
public interface ItenantIdentityPurger
{
    Task PurgeAsync(Guid tenantId, string? logoUrl, CancellationToken cancellationToken = default);
}
