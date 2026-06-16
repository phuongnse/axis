using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record TeamAccountVerified(Guid TeamAccountId) : IDomainEvent;
