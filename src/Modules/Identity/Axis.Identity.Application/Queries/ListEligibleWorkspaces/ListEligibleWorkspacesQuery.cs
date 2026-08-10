using Axis.Shared.Application.CQRS;
namespace Axis.Identity.Application.Queries.ListEligibleWorkspaces;

public sealed record ListEligibleWorkspacesQuery(Guid UserId, Guid? CurrentWorkspaceId) : IQuery<IReadOnlyList<EligibleWorkspaceDto>>;
public sealed record EligibleWorkspaceDto(Guid WorkspaceId, string Name, string Slug, string Type, Guid? OrganizationId, bool IsCurrent);
