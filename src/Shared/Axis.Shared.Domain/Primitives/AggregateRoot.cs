namespace Axis.Shared.Domain.Primitives;

/// <summary>
/// Base class for aggregate roots. Extends Entity with domain event collection.
/// Modules raise domain events here; they are dispatched after persistence by the infrastructure layer.
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
