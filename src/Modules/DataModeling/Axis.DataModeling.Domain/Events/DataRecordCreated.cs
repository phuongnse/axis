using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataRecordCreated(Guid RecordId, Guid ModelId, Guid TeamAccountId) : IDomainEvent;
