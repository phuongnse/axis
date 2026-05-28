namespace Axis.Identity.Application.Services;

/// <summary>Removes platform-level Identity records for a hard-deleted organization.</summary>
public interface IOrganizationIdentityPurger
{
    Task PurgeAsync(Guid organizationId, string? logoUrl, CancellationToken cancellationToken = default);
}
