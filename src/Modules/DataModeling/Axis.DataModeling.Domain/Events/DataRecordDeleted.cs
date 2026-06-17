using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataRecordDeleted(Guid RecordId, Guid ModelId, Guid workspaceId) : IDomainEvent;
