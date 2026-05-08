using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
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
        var events = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>())
            entry.Entity.ClearDomainEvents();

        var result = await context.SaveChangesAsync(ct);

        foreach (var evt in events)
            await bus.PublishAsync(evt);

        return result;
    }
}
