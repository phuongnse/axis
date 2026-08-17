using Axis.Identity.Application;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Shared.Application;
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
        WorkspaceInvitationSortField? sortBy = null,
        CollectionSortDirection? sortDirection = null,
        CancellationToken ct = default) =>
        await Order(
                Query()
            .AsNoTracking()
                    .Where(invitation => invitation.WorkspaceId == workspaceId),
                sortBy,
                sortDirection)
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

    private static IOrderedQueryable<WorkspaceInvitation> Order(
        IQueryable<WorkspaceInvitation> invitations,
        WorkspaceInvitationSortField? sortBy,
        CollectionSortDirection? sortDirection)
    {
        if (!sortBy.HasValue && !sortDirection.HasValue)
        {
            return invitations
                .OrderByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id);
        }

        if (!sortBy.HasValue || !sortDirection.HasValue)
            throw new ArgumentException("Invitation sort field and direction must be supplied together.");

        return (sortBy.Value, sortDirection.Value) switch
        {
            (WorkspaceInvitationSortField.Email, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.NormalizedEmail == null)
                .ThenBy(invitation => invitation.NormalizedEmail)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Email, CollectionSortDirection.Descending) => invitations
                .OrderBy(invitation => invitation.NormalizedEmail == null)
                .ThenByDescending(invitation => invitation.NormalizedEmail)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Status, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.Status)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Status, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.Status)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Role, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.RequestedRole)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Role, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.RequestedRole)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Created, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Created, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Expires, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.ExpiresAt)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Expires, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.ExpiresAt)
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Delivery, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.TokenGenerations
                    .OrderByDescending(token => token.Generation)
                    .Select(token => token.DeliveryStatus)
                    .First())
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Delivery, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.TokenGenerations
                    .OrderByDescending(token => token.Generation)
                    .Select(token => token.DeliveryStatus)
                    .First())
                .ThenByDescending(invitation => invitation.CreatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Revision, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.Revision)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.Revision, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.Revision)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.CreatedBy, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => EF.Property<string>(invitation, "CreatedByDisplayName") == null)
                .ThenBy(invitation => EF.Property<string>(invitation, "CreatedByDisplayName"))
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.CreatedBy, CollectionSortDirection.Descending) => invitations
                .OrderBy(invitation => EF.Property<string>(invitation, "CreatedByDisplayName") == null)
                .ThenByDescending(invitation => EF.Property<string>(invitation, "CreatedByDisplayName"))
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.ModifiedBy, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => EF.Property<string>(invitation, "UpdatedByDisplayName") == null)
                .ThenBy(invitation => EF.Property<string>(invitation, "UpdatedByDisplayName"))
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.ModifiedBy, CollectionSortDirection.Descending) => invitations
                .OrderBy(invitation => EF.Property<string>(invitation, "UpdatedByDisplayName") == null)
                .ThenByDescending(invitation => EF.Property<string>(invitation, "UpdatedByDisplayName"))
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.ModifiedAt, CollectionSortDirection.Ascending) => invitations
                .OrderBy(invitation => invitation.UpdatedAt)
                .ThenBy(invitation => invitation.Id),
            (WorkspaceInvitationSortField.ModifiedAt, CollectionSortDirection.Descending) => invitations
                .OrderByDescending(invitation => invitation.UpdatedAt)
                .ThenBy(invitation => invitation.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "Invitation sort is invalid."),
        };
    }
}
