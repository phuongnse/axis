using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormCreated(Guid FormId, Guid TeamAccountId, string Name) : IDomainEvent;
