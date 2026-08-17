using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Application;

namespace Axis.Identity.Application.Repositories;

public interface IWorkspaceMembershipRepository
{
    Task AddAsync(WorkspaceMembership membership, CancellationToken ct = default);
    Task<WorkspaceMembership?> GetActiveAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);
    Task<WorkspaceMembership?> GetActiveHumanAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);
    Task<WorkspaceMembership?> GetAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceMembership>> ListActiveForUserAsync(
        Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ActiveWorkspaceHumanProjection>> ListActiveForWorkspaceAsync(
        Guid workspaceId,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ActiveWorkspaceHumanProjection>>([]);
    Task<bool> HasActivePersonalOwnerWorkspaceAsync(
        Guid userId,
        CancellationToken ct = default);
    Task<bool> HasActiveWorkspaceAccessAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<EligibleWorkspaceProjection>> ListEligibleWorkspacesAsync(
        Guid userId,
        CancellationToken ct = default);
}

public sealed record EligibleWorkspaceProjection(
    Guid WorkspaceId,
    string Name,
    WorkspaceSlug Slug,
    WorkspaceType Type,
    Guid? OrganizationId);

public sealed record ActiveWorkspaceHumanProjection(
    Guid UserId,
    string DisplayName,
    string Email,
    WorkspaceMembershipRole WorkspaceRole,
    bool IsProductBuilder,
    int MembershipRevision,
    ResourceMetadataDto Metadata);
