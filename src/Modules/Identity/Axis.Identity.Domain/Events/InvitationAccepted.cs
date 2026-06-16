using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record InvitationAccepted(
    Guid InvitationId,
    Guid TeamAccountId,
    string Email) : IDomainEvent;
