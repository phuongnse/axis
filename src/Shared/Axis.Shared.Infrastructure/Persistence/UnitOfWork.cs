using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using Wolverine;

namespace Axis.Shared.Infrastructure.Persistence;

/// <summary>
/// Base UnitOfWork implementation.
/// Collects domain events from all tracked aggregates, persists changes,
/// then publishes events via Wolverine (outbox-backed when configured).
/// Events are cleared from aggregates before save to prevent double-dispatch
/// if SaveChangesAsync is called more than once.
/// </summary>
public abstract class UnitOfWork(DbContext context, IMessageBus bus)
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        List<IDomainEvent> events = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (EntityEntry<IHasDomainEvents> entry in context.ChangeTracker.Entries<IHasDomainEvents>())
            entry.Entity.ClearDomainEvents();

        int result;
        try
        {
            result = await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // xmin mismatch: another Wolverine worker committed a change to the same row
            // between our load and our save. Callers (step handlers) treat this as a signal
            // that the concurrent instance already processed the message — exit gracefully.
            throw new ConcurrencyException(ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            throw new UniqueConstraintException(
                "A record with a conflicting unique key already exists.", ex);
        }

        foreach (IDomainEvent evt in events)
            await bus.PublishAsync(evt);

        return result;
    }
}
