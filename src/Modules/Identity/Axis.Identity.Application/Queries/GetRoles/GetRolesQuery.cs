using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetRoles;

/// <summary>Returns a paginated list of roles (custom + system) for a team account.</summary>
public sealed record GetRolesQuery(Guid TeamAccountId, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<RoleDto>>;
