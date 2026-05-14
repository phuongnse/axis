using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record ModelDeleted(Guid ModelId, Guid OrganizationId) : IDomainEvent;
