using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataRecordCreated(Guid RecordId, Guid ModelId, Guid OrganizationId) : IDomainEvent;
public sealed record DataRecordDeleted(Guid RecordId, Guid ModelId, Guid OrganizationId) : IDomainEvent;
