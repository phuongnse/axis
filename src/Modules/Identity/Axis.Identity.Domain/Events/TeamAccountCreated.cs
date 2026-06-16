using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record TeamAccountCreated(
    Guid TeamAccountId,
    string Name,
    string Slug,
    string OwnerEmail) : IDomainEvent;
