using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record InvitationCreated(
    Guid InvitationId,
    Guid TeamAccountId,
    string Email,
    string Token) : IDomainEvent;
