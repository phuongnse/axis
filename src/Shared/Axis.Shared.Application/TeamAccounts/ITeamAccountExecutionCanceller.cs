namespace Axis.Shared.Application.TeamAccounts;

/// <summary>Cancels in-flight workflow executions before team account hard-delete.</summary>
public interface ITeamAccountExecutionCanceller
{
    Task CancelAllForTeamAccountAsync(Guid teamAccountId, CancellationToken cancellationToken = default);
}
