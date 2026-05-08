namespace Axis.Identity.Application.Queries.GetRoles;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions);
