using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Queries.ReviewWorkspaceInvitation;

public sealed class ReviewWorkspaceInvitationHandler(
    IUserRepository users,
    IOrganizationRepository organizations,
    IWorkspaceRepository workspaces,
    IWorkspaceInvitationRepository invitations,
    IIdentityAuditOutbox auditOutbox,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : IQueryHandler<ReviewWorkspaceInvitationQuery, Result<WorkspaceInvitationReviewDto>>
{
    public async Task<Result<WorkspaceInvitationReviewDto>> Handle(
        ReviewWorkspaceInvitationQuery query,
        CancellationToken ct)
    {
        if (query.UserId == Guid.Empty || string.IsNullOrWhiteSpace(query.HandoffHash))
            return Invalid();

        WorkspaceInvitation? invitation = await invitations.GetByHandoffHashAsync(
            query.HandoffHash,
            ct);
        if (invitation is null)
            return Invalid();

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        InvitationAcceptanceOutcome classification = invitation.ClassifyHandoff(
            query.HandoffHash,
            now);
        if (classification != InvitationAcceptanceOutcome.Accepted)
            return await PersistRejected(invitation, query, classification, now, ct);

        User? user = await users.GetByIdPlatformWideAsync(query.UserId, ct);
        if (user is not { Status: UserStatus.Active, IsEmailVerified: true }
            || !invitation.IsTargetEmail(user.Email.Value))
        {
            return await PersistRejected(
                invitation,
                query,
                InvitationAcceptanceOutcome.Unknown,
                now,
                ct,
                IdentityProblemCodes.InvitationAccountMismatch);
        }

        Organization? organization = await organizations.GetByIdAsync(invitation.OrganizationId, ct);
        Workspace? workspace = await workspaces.GetByIdAsync(invitation.WorkspaceId, ct);
        User? inviter = await users.GetByIdPlatformWideAsync(invitation.InviterUserId, ct);
        if (organization is null
            || workspace is not { Type: WorkspaceType.Organization, Status: WorkspaceStatus.Active }
            || workspace.OrganizationId != invitation.OrganizationId
            || inviter is null)
        {
            return await PersistRejected(
                invitation,
                query,
                InvitationAcceptanceOutcome.Unknown,
                now,
                ct,
                IdentityProblemCodes.InvitationAuthorityStale);
        }

        return Result.Success(new WorkspaceInvitationReviewDto(
            invitation.Id,
            invitation.WorkspaceId,
            organization.Name,
            workspace.Name,
            inviter.FullName,
            invitation.RequestedRole.ToString(),
            invitation.ExpiresAt));
    }

    private async Task<Result<WorkspaceInvitationReviewDto>> PersistRejected(
        WorkspaceInvitation invitation,
        ReviewWorkspaceInvitationQuery query,
        InvitationAcceptanceOutcome outcome,
        DateTime now,
        CancellationToken ct,
        string? problemCode = null)
    {
        Guid auditEventId = Guid.NewGuid();
        await auditOutbox.EnqueueAsync(
            WorkspaceInvitationAudit.Create(
                auditEventId,
                AuditActorKindV1.Human,
                query.UserId,
                query.UserId,
                invitation,
                "workspace.invitation.review_rejected",
                outcome.ToString().ToLowerInvariant(),
                query.CorrelationId,
                now),
            ct);
        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return Result.Failure<WorkspaceInvitationReviewDto>(
                ErrorCodes.BusinessRule,
                "Invitation outcome could not be audited.",
                IdentityProblemCodes.InvitationAuditUnavailable);
        }

        return Result.Failure<WorkspaceInvitationReviewDto>(
            problemCode == IdentityProblemCodes.InvitationAccountMismatch
                ? ErrorCodes.Forbidden
                : ErrorCodes.Conflict,
            "Invitation access is invalid or no longer available.",
            problemCode ?? IdentityProblemCodes.InvitationAccessInvalid);
    }

    private static Result<WorkspaceInvitationReviewDto> Invalid() =>
        Result.Failure<WorkspaceInvitationReviewDto>(
            ErrorCodes.InvalidInput,
            "Invitation access is invalid or no longer available.",
            IdentityProblemCodes.InvitationAccessInvalid);
}
