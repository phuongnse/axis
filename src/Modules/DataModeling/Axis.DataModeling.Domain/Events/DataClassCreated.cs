using Axis.Shared.Domain.Primitives;

namespace Axis.DataModeling.Domain.Events;

public sealed record DataClassCreated(Guid DataClassId, Guid OrganizationId, string Name) : IDomainEvent;
