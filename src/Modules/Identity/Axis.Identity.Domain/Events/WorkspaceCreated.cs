using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record WorkspaceCreated(
    Guid workspaceId,
    string Name,
    string Slug,
    string OwnerEmail) : IDomainEvent;
