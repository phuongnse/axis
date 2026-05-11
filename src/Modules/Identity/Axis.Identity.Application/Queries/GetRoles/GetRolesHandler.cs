using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetRoles;

/// <summary>US-021: Lists all roles for the org, including system roles, paginated.</summary>
public sealed class GetRolesHandler(IRoleRepository roleRepo)
    : IQueryHandler<GetRolesQuery, PagedResult<RoleDto>>
{
    public async Task<PagedResult<RoleDto>> Handle(GetRolesQuery query, CancellationToken cancellationToken)
    {
        int effectivePageSize = Math.Min(query.PageSize, 100);

        (IReadOnlyList<Role> items, int totalCount) =
            await roleRepo.GetPagedAsync(query.OrganizationId, query.Page, effectivePageSize, cancellationToken);

        IReadOnlyList<RoleDto> dtos = items
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions))
            .ToList();

        return new PagedResult<RoleDto>(dtos, totalCount, query.Page, effectivePageSize);
    }
}
