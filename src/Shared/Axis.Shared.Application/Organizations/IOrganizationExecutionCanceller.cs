namespace Axis.Shared.Application.Organizations;

/// <summary>Cancels in-flight workflow executions before organization hard-delete.</summary>
public interface IOrganizationExecutionCanceller
{
    Task CancelAllForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
