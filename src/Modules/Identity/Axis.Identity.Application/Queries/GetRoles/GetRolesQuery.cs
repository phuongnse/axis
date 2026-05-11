using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetRoles;

/// <summary>US-021: Returns a paginated list of roles (custom + system) for an organization.</summary>
public sealed record GetRolesQuery(Guid OrganizationId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<RoleDto>>;
