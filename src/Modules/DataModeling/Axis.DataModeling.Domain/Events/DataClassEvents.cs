using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataClassCreated(Guid DataClassId, Guid OrganizationId, string Name) : IDomainEvent;
public sealed record DataClassDeleted(Guid DataClassId, Guid OrganizationId) : IDomainEvent;
