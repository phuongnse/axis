using Axis.Identity.Domain.Aggregates;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceInvitationRepository
{
    Task AddAsync(WorkspaceInvitation invitation, CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByIdAsync(
        Guid workspaceId,
        Guid invitationId,
        CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetCanonicalPendingAsync(
        Guid workspaceId,
        string normalizedEmail,
        WorkspaceMembershipRole role,
        CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<WorkspaceInvitation?> GetByHandoffHashAsync(string handoffHash, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceInvitation>> ListForWorkspaceAsync(
        Guid workspaceId,
        int offset,
        int limit,
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
