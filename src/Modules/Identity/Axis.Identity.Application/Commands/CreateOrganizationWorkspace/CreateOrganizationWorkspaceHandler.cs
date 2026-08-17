using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.CreateOrganizationWorkspace;

public sealed class CreateOrganizationWorkspaceHandler(
    IUserRepository users,
    IOrganizationRepository organizations,
    IOrganizationMembershipRepository organizationMemberships,
    IWorkspaceRepository workspaces,
    IWorkspaceMembershipRepository workspaceMemberships,
    ICreateOrganizationIdempotencyRepository idempotency,
    IWorkspaceSlugGenerator slugs,
    IIdentityAuditOutbox auditOutbox,
    IUnitOfWork uow)
    : ICommandHandler<CreateOrganizationWorkspaceCommand, CreateOrganizationWorkspaceDto>
{
    private const string CreationAuditAction = "organization.workspace.created";
    private const string CreationAuditTargetType = "Organization";
    private const string CreationAuditOutcome = "succeeded";

    public async Task<Result<CreateOrganizationWorkspaceDto>> Handle(
        CreateOrganizationWorkspaceCommand command,
        CancellationToken ct)
    {
        if (command.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || string.IsNullOrWhiteSpace(command.CorrelationId))
        {
            return Result.Failure<CreateOrganizationWorkspaceDto>(
                ErrorCodes.InvalidInput,
                "User, idempotency key, and correlation are required.");
        }

        string name;
        try
        {
            name = Normalize(command.Name);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.InvalidInput, ex.Message);
        }

        string key = command.IdempotencyKey.Trim();
        User? user = await users.GetByIdPlatformWideAsync(command.UserId, ct);
        if (user is null || user.Status != UserStatus.Active || !user.IsEmailVerified)
        {
            return Result.Failure<CreateOrganizationWorkspaceDto>(
                ErrorCodes.NotFound,
                "The verified account is not available.");
        }

        if (!await HasActivePersonalWorkspaceAsync(user.Id, ct))
        {
            return Result.Failure<CreateOrganizationWorkspaceDto>(
                ErrorCodes.Forbidden,
                "An active personal Workspace is required.");
        }

        CreateOrganizationIdempotencyRecord? prior = await idempotency.GetAsync(
            user.Id,
            key,
            ct);
        if (prior is not null)
            return await ResolvePrior(user.Id, prior, name, ct);

        Organization organization = Organization.Create(name);
        Workspace workspace = Workspace.CreateOrganization(
            name,
            await slugs.GenerateUniqueSlugAsync(name, ct),
            organization.Id);
        await organizations.AddAsync(organization, ct);
        await organizationMemberships.AddAsync(
            OrganizationMembership.Create(
                organization.Id,
                user.Id,
                OrganizationMembershipRole.Owner),
            ct);
        await workspaces.AddAsync(workspace, ct);
        WorkspaceMembership workspaceMembership = WorkspaceMembership.CreateOrganizationCreator(
            workspace.Id,
            user.Id);
        workspaceMembership.InitializeMetadata(
            ActorSnapshot.User(user.Id, user.FullName),
            DateTime.UtcNow);
        await workspaceMemberships.AddAsync(workspaceMembership, ct);
        await idempotency.AddAsync(
            user.Id,
            new CreateOrganizationIdempotencyRecord(
                key,
                name,
                organization.Id,
                workspace.Id),
            ct);
        await auditOutbox.EnqueueAsync(
            new AuditEventV1(
                organization.Id,
                AuditActorKindV1.Human,
                user.Id,
                user.Id,
                workspace.Id,
                CreationAuditAction,
                CreationAuditTargetType,
                organization.Id,
                CreationAuditOutcome,
                DateTimeOffset.UtcNow,
                command.CorrelationId.Trim(),
                new Dictionary<string, string>
                {
                    { "organizationId", organization.Id.ToString() },
                    { "workspaceId", workspace.Id.ToString() },
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
            CreateOrganizationIdempotencyRecord? concurrent = await idempotency.GetAsync(
                user.Id,
                key,
                ct);
            return concurrent is null
                ? CreationConflict()
                : await ResolvePrior(user.Id, concurrent, name, ct);
        }

        return await ReadBack(user.Id, key, name, organization.Id, workspace.Id, ct);
    }

    private async Task<Result<CreateOrganizationWorkspaceDto>> ResolvePrior(
        Guid userId,
        CreateOrganizationIdempotencyRecord prior,
        string canonicalRequest,
        CancellationToken ct) =>
        StringComparer.Ordinal.Equals(prior.CanonicalRequest, canonicalRequest)
            ? await ReadBack(
                userId,
                prior.Key,
                canonicalRequest,
                prior.OrganizationId,
                prior.WorkspaceId,
                ct)
            : IdempotencyConflict();

    private async Task<Result<CreateOrganizationWorkspaceDto>> ReadBack(
        Guid userId,
        string idempotencyKey,
        string canonicalRequest,
        Guid organizationId,
        Guid workspaceId,
        CancellationToken ct)
    {
        Organization? organization = await organizations.GetByIdAsync(organizationId, ct);
        Workspace? workspace = await workspaces.GetByIdAsync(workspaceId, ct);
        OrganizationMembership? organizationMembership =
            await organizationMemberships.GetActiveAsync(organizationId, userId, ct);
        WorkspaceMembership? workspaceMembership =
            await workspaceMemberships.GetActiveAsync(workspaceId, userId, ct);
        CreateOrganizationIdempotencyRecord? retry =
            await idempotency.GetAsync(userId, idempotencyKey, ct);
        IdentityAuditOutboxEntry? audit = await auditOutbox.GetAsync(organizationId, ct);

        if (organization is null
            || workspace is null
            || workspace.OrganizationId != organization.Id
            || organizationMembership is not
            {
                Role: OrganizationMembershipRole.Owner,
                Status: MembershipStatus.Active,
            }
            || workspaceMembership is not
            {
                Role: WorkspaceMembershipRole.Administrator,
                Status: MembershipStatus.Active,
                IsProductBuilder: true,
            }
            || retry is null
            || !StringComparer.Ordinal.Equals(retry.CanonicalRequest, canonicalRequest)
            || retry.OrganizationId != organizationId
            || retry.WorkspaceId != workspaceId
            || !IsExpectedAudit(audit, userId, organizationId, workspaceId))
        {
            return Result.Failure<CreateOrganizationWorkspaceDto>(
                ErrorCodes.BusinessRule,
                "Creation could not be confirmed.");
        }

        return Result.Success(
            new CreateOrganizationWorkspaceDto(
                organization.Id,
                organization.Name,
                workspace.Id,
                workspace.Name,
                workspace.Slug.Value));
    }

    private static bool IsExpectedAudit(
        IdentityAuditOutboxEntry? auditEntry,
        Guid userId,
        Guid organizationId,
        Guid workspaceId)
    {
        if (auditEntry is null
            || auditEntry.State is not (
                IdentityAuditOutboxState.Pending or IdentityAuditOutboxState.Delivered))
        {
            return false;
        }

        AuditEventV1 audit = auditEntry.Event;
        return audit.EventId == organizationId
        && audit.ActorKind == AuditActorKindV1.Human
        && audit.ActorId == userId
        && audit.SubjectId == userId
        && audit.WorkspaceId == workspaceId
        && StringComparer.Ordinal.Equals(audit.Action, CreationAuditAction)
        && StringComparer.Ordinal.Equals(audit.TargetType, CreationAuditTargetType)
        && audit.TargetId == organizationId
        && StringComparer.Ordinal.Equals(audit.Outcome, CreationAuditOutcome)
        && audit.Metadata is not null
        && audit.Metadata.TryGetValue("organizationId", out string? auditOrganizationId)
        && StringComparer.Ordinal.Equals(auditOrganizationId, organizationId.ToString())
        && audit.Metadata.TryGetValue("workspaceId", out string? auditWorkspaceId)
        && StringComparer.Ordinal.Equals(auditWorkspaceId, workspaceId.ToString());
    }

    private static Result<CreateOrganizationWorkspaceDto> IdempotencyConflict() =>
        Result.Failure<CreateOrganizationWorkspaceDto>(
            ErrorCodes.Conflict,
            "Idempotency key was previously used with different content.");

    private static Result<CreateOrganizationWorkspaceDto> CreationConflict() =>
        Result.Failure<CreateOrganizationWorkspaceDto>(
            ErrorCodes.Conflict,
            "Organization creation conflicted with another request. Retry with the same idempotency key.");

    private Task<bool> HasActivePersonalWorkspaceAsync(Guid userId, CancellationToken ct) =>
        workspaceMemberships.HasActivePersonalOwnerWorkspaceAsync(userId, ct);

    private static string Normalize(string value) => Organization.Create(value).Name;
}
