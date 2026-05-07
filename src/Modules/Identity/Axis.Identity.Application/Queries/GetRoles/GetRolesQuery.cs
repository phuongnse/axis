using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetRoles;

/// <summary>US-021: Returns all roles (custom + system) for an organization.</summary>
public sealed record GetRolesQuery(Guid OrganizationId) : IQuery<IReadOnlyList<RoleDto>>;
