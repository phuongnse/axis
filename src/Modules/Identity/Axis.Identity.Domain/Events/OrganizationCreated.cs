using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record OrganizationCreated(
    Guid OrganizationId,
    string Name,
    string Slug,
    string OwnerEmail) : IDomainEvent;
