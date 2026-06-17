using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record RoleAssigned(Guid UserId, Guid workspaceId, Guid RoleId) : IDomainEvent;
