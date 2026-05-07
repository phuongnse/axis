using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormCreated(Guid FormId, Guid OrganizationId, string Name) : IDomainEvent;
public sealed record FormDeleted(Guid FormId, Guid OrganizationId) : IDomainEvent;
