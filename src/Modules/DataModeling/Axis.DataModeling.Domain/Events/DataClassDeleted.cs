using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataClassDeleted(Guid DataClassId, Guid TeamAccountId) : IDomainEvent;
