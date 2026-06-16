namespace Axis.Identity.Application.Services;

/// <summary>Removes platform-level Identity records for a hard-deleted teamAccount.</summary>
public interface ITeamAccountIdentityPurger
{
    Task PurgeAsync(Guid teamAccountId, string? logoUrl, CancellationToken cancellationToken = default);
}
