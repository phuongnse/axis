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
            return Result.Success(new InviteWorkspaceMemberDto(
                "ExistingMember",
                requestedRole.ToString(),
                null));
        }

        WorkspaceInvitation? canonical = await invitations.GetCanonicalPendingAsync(
            command.WorkspaceId,
            email.Value,
            requestedRole,
            ct);
        if (canonical is not null)
            return Result.Success(ToDto(canonical, "CanonicalPending"));

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
            WorkspaceInvitation? concurrent = await invitations.GetCanonicalPendingAsync(
                command.WorkspaceId,
                email.Value,
                requestedRole,
                ct);
            return concurrent is null
                ? Result.Failure<InviteWorkspaceMemberDto>(
                    ErrorCodes.Conflict,
                    "Invitation creation conflicted with another request.",
                    IdentityProblemCodes.InvitationConflict)
                : Result.Success(ToDto(concurrent, "CanonicalPending"));
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
    {
        await auditOutbox.EnqueueAsync(
            InvitationAudit(
                Guid.NewGuid(),
                command.InviterUserId,
                command.WorkspaceId,
                "workspace.invitation.create_denied",
                outcome,
                command.CorrelationId,
                new Dictionary<string, string>
                {
                    ["organizationId"] = organizationId.ToString(),
                    ["workspaceId"] = command.WorkspaceId.ToString(),
                }),
            ct);
        try
        {
            await uow.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (Exception)
        {
            uow.ClearTracking();
            return Result.Failure(ErrorCodes.BusinessRule, "Denied invitation audit persistence failed.");
        }
    }

    private static AuditEventV1 InvitationAudit(
        Guid eventId,
        Guid actorId,
        Guid workspaceId,
        string action,
        string outcome,
        string correlationId,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            eventId,
            AuditActorKindV1.Human,
            actorId,
            actorId,
            workspaceId,
            action,
            TargetType,
            eventId,
            outcome,
            DateTimeOffset.UtcNow,
            correlationId.Trim(),
            metadata);

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
