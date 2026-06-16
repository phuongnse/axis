using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record WorkspaceVerified(Guid workspaceId) : IDomainEvent;
