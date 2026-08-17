using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceInvitationRepository
{
    Task AddAsync(WorkspaceInvitation invitation, CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByIdAsync(
        Guid workspaceId,
        Guid invitationId,
        CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetPendingForRecipientAsync(
        Guid workspaceId,
        string normalizedEmail,
        CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByHandoffHashAsync(string handoffHash, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceInvitation>> ListForWorkspaceAsync(
        Guid workspaceId,
        int offset,
        int limit,
        WorkspaceInvitationSortField? sortBy = null,
        CollectionSortDirection? sortDirection = null,
        CancellationToken ct = default);
    Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceInvitation>> ListDueDeliveryAsync(
        DateTime now,
        int limit,
        CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceInvitation>> ListDueExpiryAsync(
        DateTime now,
        int limit,
        CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceInvitation>> ListReadyForTerminalCleanupAsync(
        int limit,
        CancellationToken ct = default);
}
