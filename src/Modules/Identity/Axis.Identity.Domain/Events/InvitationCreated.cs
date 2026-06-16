using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Events;

public record InvitationCreated(
    Guid InvitationId,
    Guid OrganizationId,
    string Email,
    string Token) : IDomainEvent;
