using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleCreated(
    Guid RoleId,
    Guid workspaceId,
    string Name,
    bool IsSystem) : IDomainEvent;
