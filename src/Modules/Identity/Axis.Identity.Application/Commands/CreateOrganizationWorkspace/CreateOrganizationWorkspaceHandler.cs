using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;
namespace Axis.Identity.Application.Commands.CreateOrganizationWorkspace;

public sealed class CreateOrganizationWorkspaceHandler(IUserRepository users, IOrganizationRepository organizations, IOrganizationMembershipRepository organizationMemberships, IWorkspaceRepository workspaces, IWorkspaceMembershipRepository workspaceMemberships, ICreateOrganizationIdempotencyRepository idempotency, IWorkspaceSlugGenerator slugs, IIdentityAuditOutbox auditOutbox, IUnitOfWork uow) : ICommandHandler<CreateOrganizationWorkspaceCommand, CreateOrganizationWorkspaceDto>
{
    public async Task<Result<CreateOrganizationWorkspaceDto>> Handle(CreateOrganizationWorkspaceCommand command, CancellationToken ct)
    {
        if (command.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.IdempotencyKey) || string.IsNullOrWhiteSpace(command.CorrelationId)) return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.InvalidInput, "User, idempotency key, and correlation are required.");
        string name; try { name = Normalize(command.Name); } catch (ArgumentException ex) { return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.InvalidInput, ex.Message); }
        string key = command.IdempotencyKey.Trim(); string canonical = name;
        CreateOrganizationIdempotencyRecord? prior = await idempotency.GetAsync(key, ct);
        if (prior is not null) { if (!StringComparer.Ordinal.Equals(prior.CanonicalRequest, canonical)) return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.Conflict, "Idempotency key was previously used with different content."); return await ReadBack(prior.OrganizationId, prior.WorkspaceId, ct); }
        User? user = await users.GetByIdPlatformWideAsync(command.UserId, ct); if (user is null || user.Status != UserStatus.Active || !user.IsEmailVerified) return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.NotFound, "The verified account is not available.");
        Organization organization = Organization.Create(name); Workspace workspace = Workspace.CreateOrganization(name, await slugs.GenerateUniqueSlugAsync(name, ct), organization.Id);
        await organizations.AddAsync(organization, ct); await organizationMemberships.AddAsync(OrganizationMembership.Create(organization.Id, user.Id, OrganizationMembershipRole.Owner), ct); await workspaces.AddAsync(workspace, ct); await workspaceMemberships.AddAsync(WorkspaceMembership.CreateOrganizationMember(workspace.Id, user.Id, WorkspaceMembershipRole.Administrator), ct); await idempotency.AddAsync(new CreateOrganizationIdempotencyRecord(key, canonical, organization.Id, workspace.Id), ct); await auditOutbox.EnqueueAsync(new AuditEventV1(Guid.NewGuid(), AuditActorKindV1.Human, user.Id, user.Id, workspace.Id, "organization.workspace.created", "Organization", organization.Id, "succeeded", DateTimeOffset.UtcNow, command.CorrelationId.Trim(), new Dictionary<string, string> { { "organizationId", organization.Id.ToString() }, { "workspaceId", workspace.Id.ToString() } }), ct); await uow.SaveChangesAsync(ct); return await ReadBack(organization.Id, workspace.Id, ct);
    }
    private async Task<Result<CreateOrganizationWorkspaceDto>> ReadBack(Guid organizationId, Guid workspaceId, CancellationToken ct) { Organization? organization = await organizations.GetByIdAsync(organizationId, ct); Workspace? workspace = await workspaces.GetByIdAsync(workspaceId, ct); if (organization is null || workspace is null || workspace.OrganizationId != organization.Id) return Result.Failure<CreateOrganizationWorkspaceDto>(ErrorCodes.BusinessRule, "Creation could not be confirmed."); return Result.Success(new CreateOrganizationWorkspaceDto(organization.Id, organization.Name, workspace.Id, workspace.Name, workspace.Slug.Value)); }
    private static string Normalize(string value) { return Organization.Create(value).Name; }
}

