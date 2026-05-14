using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Domain.Events;

public sealed record FormDeleted(Guid FormId, Guid OrganizationId) : IDomainEvent;
