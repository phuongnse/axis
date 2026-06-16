using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.CreateRole;

public record CreateRoleCommand(
    Guid tenantId,
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions) : ICommand<Guid>;
