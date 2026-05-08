namespace Axis.Shared.Domain.Primitives;

/// <summary>
/// Non-generic interface allowing infrastructure to collect domain events
/// from any aggregate root via EF Core's ChangeTracker without reflection.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
