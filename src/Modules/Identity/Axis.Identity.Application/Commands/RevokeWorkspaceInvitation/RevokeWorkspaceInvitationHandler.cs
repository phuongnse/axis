using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RevokeWorkspaceInvitation;

public sealed class RevokeWorkspaceInvitationHandler(
    IOrganizationMembershipRepository organizationMemberships,
    IWorkspaceMembershipRepository workspaceMemberships,
    IWorkspaceInvitationRepository invitations,
    IIdentityAuditOutbox auditOutbox,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : ICommandHandler<RevokeWorkspaceInvitationCommand, WorkspaceInvitationLifecycleDto>
{
    public async Task<Result<WorkspaceInvitationLifecycleDto>> Handle(
        RevokeWorkspaceInvitationCommand command,
        CancellationToken ct)
    {
        WorkspaceInvitation? invitation = await invitations.GetByIdAsync(
            command.WorkspaceId,
            command.InvitationId,
            ct);
        if (invitation is null)
            return NotFound();
        if (!await HasAuthority(invitation, command.ActorUserId, ct))
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.revoke_denied",
                "authority_denied",
                Forbidden(),
                ct);
        }
        if (invitation.Status == WorkspaceInvitationStatus.Revoked)
            return Result.Success(invitation.ToLifecycleDto());
        if (invitation.Status != WorkspaceInvitationStatus.Pending)
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.revoke_rejected",
                "not_pending",
                Conflict(),
                ct);
        }

        try
        {
            invitation.Revoke(command.ExpectedRevision, timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException)
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.revoke_rejected",
                "revision_conflict",
                Conflict(),
                ct);
        }

        Guid auditEventId = Guid.NewGuid();
        await auditOutbox.EnqueueAsync(
            new AuditEventV1(
                auditEventId,
                AuditActorKindV1.Human,
                command.ActorUserId,
                command.ActorUserId,
                invitation.WorkspaceId,
                "workspace.invitation.revoked",
                "WorkspaceInvitation",
                invitation.Id,
                "succeeded",
                timeProvider.GetUtcNow(),
                command.CorrelationId.Trim(),
                new Dictionary<string, string>
                {
                    ["organizationId"] = invitation.OrganizationId.ToString(),
                    ["workspaceId"] = invitation.WorkspaceId.ToString(),
                    ["requestedRole"] = invitation.RequestedRole.ToString(),
                    ["generation"] = invitation.CurrentToken.Generation.ToString(),
                }),
            ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
            WorkspaceInvitation? concurrent = await invitations.GetByIdAsync(
                command.WorkspaceId,
                command.InvitationId,
                ct);
            return concurrent?.Status == WorkspaceInvitationStatus.Revoked
                ? Result.Success(concurrent.ToLifecycleDto())
                : Conflict();
        }

        WorkspaceInvitation? persisted = await invitations.GetByIdAsync(
            command.WorkspaceId,
            command.InvitationId,
            ct);
        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(auditEventId, ct);
        return persisted?.Status == WorkspaceInvitationStatus.Revoked
            && audit is not null
            && audit.State != IdentityAuditOutboxState.Poisoned
                ? Result.Success(persisted.ToLifecycleDto())
                : Result.Failure<WorkspaceInvitationLifecycleDto>(
                    ErrorCodes.BusinessRule,
                    "Invitation revocation could not be confirmed.",
                    IdentityProblemCodes.InvitationReadBackFailed);
    }

    private async Task<bool> HasAuthority(
        WorkspaceInvitation invitation,
        Guid actorId,
        CancellationToken ct) =>
        await organizationMemberships.GetActiveAsync(invitation.OrganizationId, actorId, ct) is not null
        && await workspaceMemberships.GetActiveAsync(invitation.WorkspaceId, actorId, ct) is
        {
            Role: WorkspaceMembershipRole.Administrator,
            Status: MembershipStatus.Active,
        };

    private async Task<Result<WorkspaceInvitationLifecycleDto>> PersistRejected(
        WorkspaceInvitation invitation,
        RevokeWorkspaceInvitationCommand command,
        string action,
        string outcome,
        Result<WorkspaceInvitationLifecycleDto> result,
        CancellationToken ct)
    {
        try
        {
            await auditOutbox.EnqueueAsync(
                new AuditEventV1(
                    Guid.NewGuid(),
                    AuditActorKindV1.Human,
                    command.ActorUserId,
                    command.ActorUserId,
                    invitation.WorkspaceId,
                    action,
                    "WorkspaceInvitation",
                    invitation.Id,
                    outcome,
                    timeProvider.GetUtcNow(),
                    command.CorrelationId.Trim(),
                    new Dictionary<string, string>
                    {
                        ["organizationId"] = invitation.OrganizationId.ToString(),
                        ["workspaceId"] = invitation.WorkspaceId.ToString(),
                        ["requestedRole"] = invitation.RequestedRole.ToString(),
                        ["generation"] = invitation.CurrentToken.Generation.ToString(),
                    }),
                ct);
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return Result.Failure<WorkspaceInvitationLifecycleDto>(
                ErrorCodes.BusinessRule,
                "Invitation outcome could not be audited.",
                IdentityProblemCodes.InvitationAuditUnavailable);
        }
    }

    private static Result<WorkspaceInvitationLifecycleDto> NotFound() =>
        Result.Failure<WorkspaceInvitationLifecycleDto>(
            ErrorCodes.NotFound,
            "Invitation was not found.",
            IdentityProblemCodes.InvitationNotFound);

    private static Result<WorkspaceInvitationLifecycleDto> Forbidden() =>
        Result.Failure<WorkspaceInvitationLifecycleDto>(
            ErrorCodes.Forbidden,
            "Invitation authority is required.",
            IdentityProblemCodes.InvitationForbidden);

    private static Result<WorkspaceInvitationLifecycleDto> Conflict() =>
        Result.Failure<WorkspaceInvitationLifecycleDto>(
            ErrorCodes.Conflict,
            "Only a pending invitation can be revoked.",
            IdentityProblemCodes.InvitationNotPending);
}
