using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record ModelCreated(Guid ModelId, Guid OrganizationId, string Name) : IDomainEvent;
public sealed record ModelDeleted(Guid ModelId, Guid OrganizationId) : IDomainEvent;
