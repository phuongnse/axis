namespace Axis.Shared.Domain.Primitives;

/// <summary>
/// Base class for aggregate roots. Extends Entity with domain event collection.
/// Modules may raise domain events here; dispatch, outbox, and integration-event
/// publication must be implemented explicitly by the owning module.
/// </summary>
public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id), IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
