namespace Axis.Shared.Application.Organizations;

/// <summary>Cancels pending form tasks before organization hard-delete (US-007).</summary>
public interface IOrganizationFormTaskCanceller
{
    Task CancelPendingForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
