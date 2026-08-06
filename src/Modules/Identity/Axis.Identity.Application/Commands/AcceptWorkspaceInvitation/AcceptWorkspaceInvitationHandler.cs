using Axis.Audit.Contracts;
using Axis.Identity.Application.Commands;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.AcceptWorkspaceInvitation;

public sealed class AcceptWorkspaceInvitationHandler(
    IUserRepository users,
    IOrganizationRepository organizations,
    IOrganizationMembershipRepository organizationMemberships,
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository workspaceMemberships,
    IWorkspaceInvitationRepository invitations,
    IIdentityAuditOutbox auditOutbox,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : ICommandHandler<AcceptWorkspaceInvitationCommand, WorkspaceInvitationAcceptanceDto>
{
    public async Task<Result<WorkspaceInvitationAcceptanceDto>> Handle(
        AcceptWorkspaceInvitationCommand command,
        CancellationToken ct)
    {
        if (command.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.HandoffHash)
            || string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return Invalid();
        }

        WorkspaceInvitation? invitation = await invitations.GetByHandoffHashAsync(
            command.HandoffHash,
            ct);
        if (invitation is null)
            return Invalid();

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        InvitationAcceptanceOutcome classification = invitation.ClassifyHandoff(
            command.HandoffHash,
            now);
        if (classification != InvitationAcceptanceOutcome.Accepted)
            return await PersistRejected(invitation, command, classification, now, ct);

        User? user = await users.GetByIdPlatformWideAsync(command.UserId, ct);
        if (user is not { Status: UserStatus.Active, IsEmailVerified: true }
            || !invitation.IsTargetEmail(user.Email.Value))
        {
            return await PersistRejected(
                invitation,
                command,
                InvitationAcceptanceOutcome.Unknown,
                now,
                ct,
                IdentityProblemCodes.InvitationAccountMismatch);
        }

        Organization? organization = await organizations.GetByIdAsync(invitation.OrganizationId, ct);
        Workspace? workspace = await workspaces.GetByIdAsync(invitation.WorkspaceId, ct);
        OrganizationMembership? inviterOrganizationMembership =
            await organizationMemberships.GetActiveAsync(
                invitation.OrganizationId,
                invitation.InviterUserId,
                ct);
        WorkspaceMembership? inviterWorkspaceMembership =
            await workspaceMemberships.GetActiveAsync(
                invitation.WorkspaceId,
                invitation.InviterUserId,
                ct);
        if (organization is null
            || workspace is not { Type: WorkspaceType.Organization, Status: WorkspaceStatus.Active }
            || workspace.OrganizationId != invitation.OrganizationId
            || inviterOrganizationMembership is null
            || inviterWorkspaceMembership is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
            })
        {
            return await PersistRejected(
                invitation,
                command,
                InvitationAcceptanceOutcome.Unknown,
                now,
                ct,
                IdentityProblemCodes.InvitationAuthorityStale);
        }

        OrganizationMembership? organizationMembership = await organizationMemberships.GetAsync(
            invitation.OrganizationId,
            command.UserId,
            ct);
        WorkspaceMembership? workspaceMembership = await workspaceMemberships.GetAsync(
            invitation.WorkspaceId,
            command.UserId,
            ct);
        if (organizationMembership?.Status == MembershipStatus.Suspended
            || workspaceMembership?.Status == MembershipStatus.Suspended)
        {
            return await PersistRejected(
                invitation,
                command,
                InvitationAcceptanceOutcome.Unknown,
                now,
                ct,
                IdentityProblemCodes.InvitationMembershipSuspended);
        }

        organizationMembership = await EstablishOrganizationMembership(
            invitation,
            command.UserId,
            organizationMembership,
            ct);
        workspaceMembership = await EstablishWorkspaceMembership(
            invitation,
            command.UserId,
            workspaceMembership,
            ct);

        InvitationAcceptanceOutcome outcome = invitation.Accept(
            command.HandoffHash,
            now,
            invitation.Revision);
        if (outcome != InvitationAcceptanceOutcome.Accepted)
            return await PersistRejected(invitation, command, outcome, now, ct);

        Guid auditEventId = Guid.NewGuid();
        await auditOutbox.EnqueueAsync(
            WorkspaceInvitationAudit.Create(
                auditEventId,
                AuditActorKindV1.Human,
                command.UserId,
                command.UserId,
                invitation,
                "workspace.invitation.accepted",
                "succeeded",
                command.CorrelationId,
                now),
            ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            return await RecoverConcurrentOutcome(command, now, ct);
        }
        catch (UniqueConstraintException)
        {
            return await RecoverConcurrentOutcome(command, now, ct);
        }

        WorkspaceInvitation? persisted = await invitations.GetByHandoffHashAsync(
            command.HandoffHash,
            ct);
        OrganizationMembership? persistedOrganization = await organizationMemberships.GetActiveAsync(
            invitation.OrganizationId,
            command.UserId,
            ct);
        WorkspaceMembership? persistedWorkspace = await workspaceMemberships.GetActiveAsync(
            invitation.WorkspaceId,
            command.UserId,
            ct);
        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(auditEventId, ct);
        if (persisted?.Status != WorkspaceInvitationStatus.Accepted
            || persistedOrganization is null
            || persistedWorkspace is null
            || audit is null
            || audit.State == IdentityAuditOutboxState.Poisoned)
        {
            return Result.Failure<WorkspaceInvitationAcceptanceDto>(
                ErrorCodes.BusinessRule,
                "Invitation acceptance could not be confirmed.",
                IdentityProblemCodes.InvitationReadBackFailed);
        }

        return Result.Success(new WorkspaceInvitationAcceptanceDto(
            "Accepted",
            persisted.WorkspaceId,
            persistedOrganization.Role.ToString(),
            persistedWorkspace.Role.ToString()));
    }

    private async Task<Result<WorkspaceInvitationAcceptanceDto>> RecoverConcurrentOutcome(
        AcceptWorkspaceInvitationCommand command,
        DateTime now,
        CancellationToken ct)
    {
        uow.ClearTracking();

        WorkspaceInvitation? persisted = await invitations.GetByHandoffHashAsync(
            command.HandoffHash,
            ct);
        if (persisted is null)
            return Invalid();

        InvitationAcceptanceOutcome outcome = persisted.ClassifyHandoff(
            command.HandoffHash,
            now);
        if (outcome == InvitationAcceptanceOutcome.Accepted)
            outcome = InvitationAcceptanceOutcome.Unknown;

        return await PersistRejected(
            persisted,
            command,
            outcome,
            now,
            ct,
            outcome == InvitationAcceptanceOutcome.Unknown
                ? IdentityProblemCodes.InvitationConflict
                : null);
    }

    private async Task<OrganizationMembership> EstablishOrganizationMembership(
        WorkspaceInvitation invitation,
        Guid userId,
        OrganizationMembership? membership,
        CancellationToken ct)
    {
        if (membership is null)
        {
            membership = OrganizationMembership.Create(
                invitation.OrganizationId,
                userId,
                OrganizationMembershipRole.Member);
            await organizationMemberships.AddAsync(membership, ct);
        }
        else if (membership.Status == MembershipStatus.Removed)
        {
            membership.RestoreBaselineFromInvitation(membership.Revision);
        }

        return membership;
    }

    private async Task<WorkspaceMembership> EstablishWorkspaceMembership(
        WorkspaceInvitation invitation,
        Guid userId,
        WorkspaceMembership? membership,
        CancellationToken ct)
    {
        if (membership is null)
        {
            membership = WorkspaceMembership.CreateOrganizationMember(
                invitation.WorkspaceId,
                userId,
                invitation.RequestedRole);
            await workspaceMemberships.AddAsync(membership, ct);
        }
        else if (membership.Status == MembershipStatus.Removed)
        {
            membership.RestoreFromInvitation(invitation.RequestedRole, membership.Revision);
        }

        return membership;
    }

    private async Task<Result<WorkspaceInvitationAcceptanceDto>> PersistRejected(
        WorkspaceInvitation invitation,
        AcceptWorkspaceInvitationCommand command,
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
                command.UserId,
                command.UserId,
                invitation,
                "workspace.invitation.accept_rejected",
                outcome.ToString().ToLowerInvariant(),
                command.CorrelationId,
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
            return Result.Failure<WorkspaceInvitationAcceptanceDto>(
                ErrorCodes.BusinessRule,
                "Invitation outcome could not be audited.",
                IdentityProblemCodes.InvitationAuditUnavailable);
        }

        return Result.Failure<WorkspaceInvitationAcceptanceDto>(
            problemCode == IdentityProblemCodes.InvitationAccountMismatch
                ? ErrorCodes.Forbidden
                : ErrorCodes.Conflict,
            "Invitation access is invalid or no longer available.",
            problemCode ?? IdentityProblemCodes.InvitationAccessInvalid);
    }

    private static Result<WorkspaceInvitationAcceptanceDto> Invalid() =>
        Result.Failure<WorkspaceInvitationAcceptanceDto>(
            ErrorCodes.InvalidInput,
            "Invitation access is invalid or no longer available.",
            IdentityProblemCodes.InvitationAccessInvalid);
}
