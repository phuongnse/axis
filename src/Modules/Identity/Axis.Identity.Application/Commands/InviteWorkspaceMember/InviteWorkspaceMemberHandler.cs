using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.InviteWorkspaceMember;

public sealed class InviteWorkspaceMemberHandler(
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
    : ICommandHandler<InviteWorkspaceMemberCommand, InviteWorkspaceMemberDto>
{
    private const string TargetType = "WorkspaceInvitation";

    public async Task<Result<InviteWorkspaceMemberDto>> Handle(
        InviteWorkspaceMemberCommand command,
        CancellationToken ct)
    {
        if (command.InviterUserId == Guid.Empty
            || command.WorkspaceId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return Invalid("Inviter, Workspace, and correlation are required.");
        }

        Result<Email> emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.FieldValidation<InviteWorkspaceMemberDto>(
                new Dictionary<string, string[]> { ["email"] = [IdentityProblemCodes.InvitationEmailInvalid] });
        }

        if (!Enum.TryParse(command.RequestedRole, ignoreCase: true, out WorkspaceMembershipRole requestedRole)
            || requestedRole is WorkspaceMembershipRole.Owner)
        {
            return Result.FieldValidation<InviteWorkspaceMemberDto>(
                new Dictionary<string, string[]> { ["requestedRole"] = [IdentityProblemCodes.InvitationRoleUnsupported] });
        }

        Workspace? workspace = await workspaces.GetByIdAsync(command.WorkspaceId, ct);
        if (workspace is null || workspace.Type != WorkspaceType.Organization || workspace.OrganizationId is not Guid organizationId)
        {
            return Result.Failure<InviteWorkspaceMemberDto>(
                ErrorCodes.BusinessRule,
                "Workspace invitations are available only for Organization Workspaces.",
                IdentityProblemCodes.InvitationWorkspaceIneligible);
        }

        OrganizationMembership? organizationMembership =
            await organizationMemberships.GetActiveAsync(organizationId, command.InviterUserId, ct);
        WorkspaceMembership? workspaceMembership =
            await workspaceMemberships.GetActiveAsync(command.WorkspaceId, command.InviterUserId, ct);
        if (organizationMembership is null
            || workspaceMembership is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
            })
        {
            Result auditResult = await PersistDeniedAudit(
                command,
                organizationId,
                "authority_denied",
                ct);
            return auditResult.IsFailure
                ? Result.Failure<InviteWorkspaceMemberDto>(
                    ErrorCodes.BusinessRule,
                    "The invitation attempt could not be confirmed.",
                    IdentityProblemCodes.InvitationAuditUnavailable)
                : Result.Failure<InviteWorkspaceMemberDto>(
                    ErrorCodes.Forbidden,
                    "Invitation authority is required.",
                    IdentityProblemCodes.InvitationForbidden);
        }

        Email email = emailResult.Value;
        User? existingUser = await users.FindByEmailGloballyAsync(email, ct);
        if (existingUser is not null
            && await workspaceMemberships.GetActiveAsync(command.WorkspaceId, existingUser.Id, ct) is not null)
        {
            Result auditResult = await PersistNoMutationAudit(
                command,
                organizationId,
                "WorkspaceInvitationAttempt",
                Guid.NewGuid(),
                "workspace.invitation.create_noop",
                "existing_member",
                requestedRole,
                ct);
            if (auditResult.IsFailure)
                return AuditUnavailable();

            return Result.Success(new InviteWorkspaceMemberDto(
                "ExistingMember",
                requestedRole.ToString(),
                null));
        }

        WorkspaceInvitation? canonical = await invitations.GetPendingForRecipientAsync(
            command.WorkspaceId,
            email.Value,
            ct);
        if (canonical is not null)
            return await ReturnPendingOutcome(command, organizationId, requestedRole, canonical, ct);

        Result limit = await rateLimiter.AcquireCreateAsync(
            command.InviterUserId,
            command.WorkspaceId,
            email.Value,
            ct);
        if (limit.IsFailure)
            return Result.Failure<InviteWorkspaceMemberDto>(
                limit.ErrorCode ?? ErrorCodes.RateLimited,
                limit.Error,
                limit.ProblemCode ?? IdentityProblemCodes.InvitationRateLimited);

        Organization? organization = await organizations.GetByIdAsync(organizationId, ct);
        User? inviter = await users.GetByIdPlatformWideAsync(command.InviterUserId, ct);
        if (organization is null || inviter is null)
            return Invalid("Invitation target is unavailable.");

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiresAt = now.Add(policy.InvitationLifetime);
        (string rawToken, string tokenHash) = OpaqueTokenGenerator.Create();
        Guid invitationId = Guid.NewGuid();
        InvitationDeliveryMessage delivery = new(
            invitationId,
            1,
            email.Value,
            rawToken,
            organization.Name,
            workspace.Name,
            inviter.FullName,
            requestedRole.ToString(),
            expiresAt,
            inviter.LanguagePreference?.Value ?? "en",
            DeliveryCorrelation(invitationId, 1));
        string envelope = envelopeProtector.Protect(delivery);
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            invitationId,
            organizationId,
            command.WorkspaceId,
            command.InviterUserId,
            email.Value,
            requestedRole,
            now,
            expiresAt,
            tokenHash,
            envelope,
            DeliveryCorrelation(invitationId, 1));

        await invitations.AddAsync(invitation, ct);
        await auditOutbox.EnqueueAsync(
            InvitationAudit(
                invitation.Id,
                command.InviterUserId,
                command.WorkspaceId,
                "workspace.invitation.created",
                "succeeded",
                command.CorrelationId,
                new Dictionary<string, string>
                {
                    ["organizationId"] = organizationId.ToString(),
                    ["workspaceId"] = command.WorkspaceId.ToString(),
                    ["requestedRole"] = requestedRole.ToString(),
                    ["generation"] = "1",
                }),
            ct);

        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (UniqueConstraintException)
        {
            uow.ClearTracking();
            WorkspaceInvitation? concurrent = await invitations.GetPendingForRecipientAsync(
                command.WorkspaceId,
                email.Value,
                ct);
            if (concurrent is null)
            {
                return Result.Failure<InviteWorkspaceMemberDto>(
                    ErrorCodes.Conflict,
                    "Invitation creation conflicted with another request.",
                    IdentityProblemCodes.InvitationConflict);
            }

            return await ReturnPendingOutcome(
                command,
                organizationId,
                requestedRole,
                concurrent,
                ct);
        }

        WorkspaceInvitation? persisted = await invitations.GetByIdAsync(
            command.WorkspaceId,
            invitation.Id,
            ct);
        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(invitation.Id, ct);
        if (persisted is null || audit is null || audit.State == IdentityAuditOutboxState.Poisoned)
        {
            return Result.Failure<InviteWorkspaceMemberDto>(
                ErrorCodes.BusinessRule,
                "Invitation creation could not be confirmed.",
                IdentityProblemCodes.InvitationReadBackFailed);
        }

        return Result.Success(ToDto(persisted, "Created"));
    }

    private async Task<Result> PersistDeniedAudit(
        InviteWorkspaceMemberCommand command,
        Guid organizationId,
        string outcome,
        CancellationToken ct)
        => await PersistNoMutationAudit(
            command,
            organizationId,
            "WorkspaceInvitationAttempt",
            Guid.NewGuid(),
            "workspace.invitation.create_denied",
            outcome,
            null,
            ct);

    private async Task<Result<InviteWorkspaceMemberDto>> ReturnPendingOutcome(
        InviteWorkspaceMemberCommand command,
        Guid organizationId,
        WorkspaceMembershipRole requestedRole,
        WorkspaceInvitation canonical,
        CancellationToken ct)
    {
        bool sameRole = canonical.RequestedRole == requestedRole;
        Result auditResult = await PersistNoMutationAudit(
            command,
            organizationId,
            TargetType,
            canonical.Id,
            sameRole
                ? "workspace.invitation.create_noop"
                : "workspace.invitation.create_rejected",
            sameRole ? "canonical_pending" : "pending_role_conflict",
            requestedRole,
            ct);
        if (auditResult.IsFailure)
            return AuditUnavailable();

        return sameRole
            ? Result.Success(ToDto(canonical, "CanonicalPending"))
            : Result.Failure<InviteWorkspaceMemberDto>(
                ErrorCodes.Conflict,
                "A pending invitation already exists for this recipient with a different role.",
                IdentityProblemCodes.InvitationConflict);
    }

    private async Task<Result> PersistNoMutationAudit(
        InviteWorkspaceMemberCommand command,
        Guid organizationId,
        string targetType,
        Guid targetId,
        string action,
        string outcome,
        WorkspaceMembershipRole? requestedRole,
        CancellationToken ct)
    {
        Guid eventId = Guid.NewGuid();
        Dictionary<string, string> metadata = new()
        {
            ["organizationId"] = organizationId.ToString(),
            ["workspaceId"] = command.WorkspaceId.ToString(),
        };
        if (requestedRole is not null)
            metadata["requestedRole"] = requestedRole.Value.ToString();

        await auditOutbox.EnqueueAsync(
            InvitationAudit(
                eventId,
                command.InviterUserId,
                command.WorkspaceId,
                action,
                outcome,
                command.CorrelationId,
                metadata,
                targetType,
                targetId),
            ct);
        try
        {
            await uow.SaveChangesAsync(ct);
            uow.ClearTracking();
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return Result.Failure(ErrorCodes.BusinessRule, "Invitation audit persistence failed.");
        }

        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(eventId, ct);
        return audit is null || audit.State == IdentityAuditOutboxState.Poisoned
            ? Result.Failure(ErrorCodes.BusinessRule, "Invitation audit read-back failed.")
            : Result.Success();
    }

    private static AuditEventV1 InvitationAudit(
        Guid eventId,
        Guid actorId,
        Guid workspaceId,
        string action,
        string outcome,
        string correlationId,
        IReadOnlyDictionary<string, string> metadata,
        string targetType = TargetType,
        Guid? targetId = null) =>
        new(
            eventId,
            AuditActorKindV1.Human,
            actorId,
            actorId,
            workspaceId,
            action,
            targetType,
            targetId ?? eventId,
            outcome,
            DateTimeOffset.UtcNow,
            correlationId.Trim(),
            metadata);

    private static Result<InviteWorkspaceMemberDto> AuditUnavailable() =>
        Result.Failure<InviteWorkspaceMemberDto>(
            ErrorCodes.BusinessRule,
            "The invitation outcome could not be confirmed.",
            IdentityProblemCodes.InvitationAuditUnavailable);

    private static InviteWorkspaceMemberDto ToDto(WorkspaceInvitation invitation, string outcome) =>
        new(
            outcome,
            invitation.RequestedRole.ToString(),
            invitation.ToLifecycleDto());

    private static Result<InviteWorkspaceMemberDto> Invalid(string detail) =>
        Result.Failure<InviteWorkspaceMemberDto>(
            ErrorCodes.InvalidInput,
            detail,
            IdentityProblemCodes.InvitationInvalid);

    private static string DeliveryCorrelation(Guid invitationId, int generation) =>
        $"workspace-invitation:{invitationId:N}:{generation}";
}
