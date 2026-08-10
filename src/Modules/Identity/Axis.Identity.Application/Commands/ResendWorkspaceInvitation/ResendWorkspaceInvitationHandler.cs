using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.ResendWorkspaceInvitation;

public sealed class ResendWorkspaceInvitationHandler(
    IUserRepository users,
    IOrganizationRepository organizations,
    IOrganizationMembershipRepository organizationMemberships,
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository workspaceMemberships,
    IWorkspaceInvitationRepository invitations,
    IWorkspaceInvitationRateLimiter rateLimiter,
    IInvitationDeliveryEnvelopeProtector envelopeProtector,
    IIdentityAuditOutbox auditOutbox,
    WorkspaceInvitationPolicy policy,
    TimeProvider timeProvider,
    IUnitOfWork uow)
    : ICommandHandler<ResendWorkspaceInvitationCommand, WorkspaceInvitationLifecycleDto>
{
    public async Task<Result<WorkspaceInvitationLifecycleDto>> Handle(
        ResendWorkspaceInvitationCommand command,
        CancellationToken ct)
    {
        WorkspaceInvitation? invitation = await invitations.GetByIdAsync(
            command.WorkspaceId,
            command.InvitationId,
            ct);
        if (invitation is null)
            return NotFound();

        if (!await HasAuthority(invitation.OrganizationId, command.WorkspaceId, command.ActorUserId, ct))
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.resend_denied",
                "authority_denied",
                Forbidden(),
                ct);
        }
        if (invitation.Status != WorkspaceInvitationStatus.Pending || invitation.NormalizedEmail is null)
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.resend_rejected",
                "not_pending",
                NotPending(),
                ct);
        }

        Result limit = await rateLimiter.AcquireResendAsync(
            command.ActorUserId,
            invitation.Id,
            ct);
        if (limit.IsFailure)
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.resend_rejected",
                "rate_limited",
                Result.Failure<WorkspaceInvitationLifecycleDto>(
                    limit.ErrorCode ?? ErrorCodes.RateLimited,
                    limit.Error,
                    limit.ProblemCode ?? IdentityProblemCodes.InvitationRateLimited),
                ct);
        }

        Organization? organization = await organizations.GetByIdAsync(invitation.OrganizationId, ct);
        Workspace? workspace = await workspaces.GetByIdAsync(invitation.WorkspaceId, ct);
        User? inviter = await users.GetByIdPlatformWideAsync(invitation.InviterUserId, ct);
        if (organization is null || workspace is null || inviter is null)
            return NotFound();

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiresAt = now.Add(policy.InvitationLifetime);
        int generation = invitation.CurrentToken.Generation + 1;
        (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
        InvitationDeliveryMessage message = new(
            invitation.Id,
            generation,
            invitation.NormalizedEmail,
            rawToken,
            organization.Name,
            workspace.Name,
            inviter.FullName,
            invitation.RequestedRole.ToString(),
            expiresAt,
            inviter.LanguagePreference?.Value ?? "en",
            $"workspace-invitation:{invitation.Id:N}:{generation}");

        try
        {
            invitation.Resend(
                command.ExpectedRevision,
                now,
                expiresAt,
                tokenHash,
                envelopeProtector.Protect(message),
                $"workspace-invitation:{invitation.Id:N}:{generation}");
        }
        catch (InvalidOperationException)
        {
            return await PersistRejected(
                invitation,
                command,
                "workspace.invitation.resend_rejected",
                "revision_conflict",
                Conflict(),
                ct);
        }

        Guid auditEventId = Guid.NewGuid();
        await auditOutbox.EnqueueAsync(
            Audit(
                auditEventId,
                command.ActorUserId,
                invitation,
                "workspace.invitation.resent",
                "succeeded",
                command.CorrelationId,
                generation),
            ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (ConcurrencyException)
        {
            uow.ClearTracking();
            return Conflict();
        }

        WorkspaceInvitation? persisted = await invitations.GetByIdAsync(
            command.WorkspaceId,
            command.InvitationId,
            ct);
        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(auditEventId, ct);
        return persisted is not null && audit is not null && audit.State != IdentityAuditOutboxState.Poisoned
            ? Result.Success(persisted.ToLifecycleDto())
            : Result.Failure<WorkspaceInvitationLifecycleDto>(
                ErrorCodes.BusinessRule,
                "Invitation resend could not be confirmed.",
                IdentityProblemCodes.InvitationReadBackFailed);
    }

    private async Task<bool> HasAuthority(
        Guid organizationId,
        Guid workspaceId,
        Guid actorId,
        CancellationToken ct) =>
        await organizationMemberships.GetActiveAsync(organizationId, actorId, ct) is not null
        && await workspaceMemberships.GetActiveAsync(workspaceId, actorId, ct) is
        {
            Role: WorkspaceMembershipRole.Administrator,
            Status: MembershipStatus.Active,
        };

    private async Task<Result<WorkspaceInvitationLifecycleDto>> PersistRejected(
        WorkspaceInvitation invitation,
        ResendWorkspaceInvitationCommand command,
        string action,
        string outcome,
        Result<WorkspaceInvitationLifecycleDto> result,
        CancellationToken ct)
    {
        try
        {
            await auditOutbox.EnqueueAsync(
                Audit(
                    Guid.NewGuid(),
                    command.ActorUserId,
                    invitation,
                    action,
                    outcome,
                    command.CorrelationId,
                    invitation.CurrentToken.Generation),
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

    private static AuditEventV1 Audit(
        Guid eventId,
        Guid actorId,
        WorkspaceInvitation invitation,
        string action,
        string outcome,
        string correlationId,
        int generation) =>
        new(
            eventId,
            AuditActorKindV1.Human,
            actorId,
            actorId,
            invitation.WorkspaceId,
            action,
            "WorkspaceInvitation",
            invitation.Id,
            outcome,
            DateTimeOffset.UtcNow,
            correlationId.Trim(),
            new Dictionary<string, string>
            {
                ["organizationId"] = invitation.OrganizationId.ToString(),
                ["workspaceId"] = invitation.WorkspaceId.ToString(),
                ["requestedRole"] = invitation.RequestedRole.ToString(),
                ["generation"] = generation.ToString(),
            });

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

    private static Result<WorkspaceInvitationLifecycleDto> NotPending() =>
        Result.Failure<WorkspaceInvitationLifecycleDto>(
            ErrorCodes.Conflict,
            "Only a pending invitation can be resent.",
            IdentityProblemCodes.InvitationNotPending);

    private static Result<WorkspaceInvitationLifecycleDto> Conflict() =>
        Result.Failure<WorkspaceInvitationLifecycleDto>(
            ErrorCodes.Conflict,
            "Invitation state changed. Refresh and try again.",
            IdentityProblemCodes.InvitationConflict);
}
