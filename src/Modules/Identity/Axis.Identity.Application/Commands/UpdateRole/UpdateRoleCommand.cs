using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.UpdateRole;

/// <summary>Update name, description, and permissions of a custom role.</summary>
public sealed record UpdateRoleCommand(
    Guid RoleId,
    Guid TeamAccountId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand;
