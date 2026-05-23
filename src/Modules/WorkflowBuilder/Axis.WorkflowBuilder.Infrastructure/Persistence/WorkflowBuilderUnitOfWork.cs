using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using Axis.WorkflowBuilder.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using Wolverine;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence;

/// <summary>
/// Collects domain events from tracked aggregates, persists the change set,
/// then publishes events via Wolverine. Inlined per module per ADR-017.
/// </summary>
internal sealed class WorkflowBuilderUnitOfWork(WorkflowBuilderDbContext context, IMessageBus bus) : IUnitOfWork
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
