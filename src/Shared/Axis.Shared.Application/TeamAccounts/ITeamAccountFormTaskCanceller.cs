namespace Axis.Shared.Application.TeamAccounts;

/// <summary>Cancels pending form tasks before team account hard-delete.</summary>
public interface ITeamAccountFormTaskCanceller
{
    Task CancelPendingForTeamAccountAsync(Guid teamAccountId, CancellationToken cancellationToken = default);
}
