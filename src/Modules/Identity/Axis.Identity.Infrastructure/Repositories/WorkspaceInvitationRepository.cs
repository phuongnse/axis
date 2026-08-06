using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class WorkspaceInvitationRepository(IdentityDbContext context)
    : IWorkspaceInvitationRepository
{
    public async Task AddAsync(WorkspaceInvitation invitation, CancellationToken ct = default) =>
        await context.WorkspaceInvitations.AddAsync(invitation, ct);

    public Task<WorkspaceInvitation?> GetByIdAsync(
        Guid workspaceId,
        Guid invitationId,
        CancellationToken ct = default) =>
        Query().SingleOrDefaultAsync(
            invitation => invitation.WorkspaceId == workspaceId && invitation.Id == invitationId,
            ct);

    public Task<WorkspaceInvitation?> GetPendingForRecipientAsync(
        Guid workspaceId,
        string normalizedEmail,
        CancellationToken ct = default) =>
        Query().SingleOrDefaultAsync(
            invitation => invitation.WorkspaceId == workspaceId
                && invitation.NormalizedEmail == normalizedEmail
                && invitation.Status == WorkspaceInvitationStatus.Pending,
            ct);

    public Task<WorkspaceInvitation?> GetByTokenHashAsync(
        string tokenHash,
        CancellationToken ct = default) =>
        Query().SingleOrDefaultAsync(
            invitation => invitation.TokenGenerations.Any(token => token.TokenHash == tokenHash),
            ct);

    public Task<WorkspaceInvitation?> GetByHandoffHashAsync(
        string handoffHash,
        CancellationToken ct = default) =>
        Query().SingleOrDefaultAsync(
            invitation => invitation.Handoffs.Any(handoff => handoff.HandoffHash == handoffHash),
            ct);

    public async Task<IReadOnlyList<WorkspaceInvitation>> ListForWorkspaceAsync(
        Guid workspaceId,
        int offset,
        int limit,
        CancellationToken ct = default) =>
        await Query()
            .AsNoTracking()
            .Where(invitation => invitation.WorkspaceId == workspaceId)
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ThenBy(invitation => invitation.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

    public Task<int> CountForWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
        context.WorkspaceInvitations.CountAsync(invitation => invitation.WorkspaceId == workspaceId, ct);

    public async Task<IReadOnlyList<WorkspaceInvitation>> ListDueDeliveryAsync(
        DateTime now,
        int limit,
        CancellationToken ct = default) =>
        await Query()
            .Where(invitation => invitation.Status == WorkspaceInvitationStatus.Pending)
            .Where(invitation => invitation.TokenGenerations.Any(token =>
                (token.Status == InvitationTokenStatus.Active
                    || token.Status == InvitationTokenStatus.Exchanged)
                && token.DeliveryStatus == InvitationDeliveryStatus.Pending
                && (token.NextDeliveryAttemptAt == null || token.NextDeliveryAttemptAt <= now)))
            .OrderBy(invitation => invitation.CreatedAt)
            .ThenBy(invitation => invitation.Id)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkspaceInvitation>> ListDueExpiryAsync(
        DateTime now,
        int limit,
        CancellationToken ct = default) =>
        await Query()
            .Where(invitation => invitation.Status == WorkspaceInvitationStatus.Pending
                && invitation.ExpiresAt <= now)
            .OrderBy(invitation => invitation.ExpiresAt)
            .ThenBy(invitation => invitation.Id)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkspaceInvitation>> ListReadyForTerminalCleanupAsync(
        int limit,
        CancellationToken ct = default) =>
        await Query()
            .Where(invitation => invitation.Status != WorkspaceInvitationStatus.Pending
                && invitation.TerminalMaterialPurgedAt == null)
            .Where(invitation => context.IdentityAuditOutboxRecords.Any(audit =>
                audit.TargetId == invitation.Id
                && audit.Status == IdentityAuditOutboxStatus.Delivered
                && audit.Outcome == "succeeded"
                && ((invitation.Status == WorkspaceInvitationStatus.Accepted
                        && audit.Action == "workspace.invitation.accepted")
                    || (invitation.Status == WorkspaceInvitationStatus.Revoked
                        && audit.Action == "workspace.invitation.revoked")
                    || (invitation.Status == WorkspaceInvitationStatus.Expired
                        && audit.Action == "workspace.invitation.expired"))))
            .OrderBy(invitation => invitation.CreatedAt)
            .ThenBy(invitation => invitation.Id)
            .Take(limit)
            .ToListAsync(ct);

    private IQueryable<WorkspaceInvitation> Query() =>
        context.WorkspaceInvitations
            .Include(invitation => invitation.TokenGenerations)
            .Include(invitation => invitation.Handoffs);
}
