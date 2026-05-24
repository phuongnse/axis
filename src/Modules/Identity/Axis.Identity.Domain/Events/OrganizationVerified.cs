using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record OrganizationVerified(Guid OrganizationId) : IDomainEvent;
