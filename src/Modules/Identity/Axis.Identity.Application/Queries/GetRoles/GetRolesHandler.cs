using Axis.Identity.Application.Repositories;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetRoles;

/// <summary>US-021: Lists all roles for the org, including system roles.</summary>
public sealed class GetRolesHandler(IRoleRepository roleRepo)
    : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await roleRepo.GetAllAsync(query.OrganizationId, cancellationToken);

        return roles
            .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions))
            .ToList()
            .AsReadOnly();
    }
}
