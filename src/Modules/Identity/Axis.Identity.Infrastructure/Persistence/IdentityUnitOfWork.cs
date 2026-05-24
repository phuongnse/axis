using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Messaging;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using Wolverine;

namespace Axis.Identity.Infrastructure.Persistence;

/// <summary>
/// Collects domain events from tracked aggregates, persists the change set,
/// then publishes events via Wolverine (outbox-backed when configured).
/// Events are cleared before save so a re-invocation of SaveChangesAsync
/// does not double-dispatch.
///
/// Per ADR-017 the logic is inlined per module rather than inheriting from
/// a Axis.Shared.Infrastructure base — explicit per-module ownership over
/// magical inheritance.
/// </summary>
internal sealed class IdentityUnitOfWork(IdentityDbContext context, IMessageBus bus) : IUnitOfWork
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
            // between load and save. Callers (step handlers) treat this as a signal that
            // the concurrent instance already processed the message — exit gracefully.
            throw new ConcurrencyException(ex);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            throw new UniqueConstraintException(
                "A record with a conflicting unique key already exists.", ex);
        }

        // Note: Wolverine's IMessageBus.PublishAsync signature is
        // PublishAsync(object, DeliveryOptions?) — fire-and-forget enqueue
        // onto the outbox; no CancellationToken overload exists. The
        // outbox dispatch happens later out of this scope.
        foreach (IDomainEvent evt in events)
        {
            object? integrationEvent = IdentityEventMapper.ToIntegrationEvent(evt);
            if (integrationEvent is not null)
                await bus.PublishAsync(integrationEvent);
        }

        return result;
    }
}
