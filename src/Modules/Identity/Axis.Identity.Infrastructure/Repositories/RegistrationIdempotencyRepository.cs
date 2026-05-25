using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class RegistrationIdempotencyRepository(IdentityDbContext context)
    : IRegistrationIdempotencyRepository
{
    private static readonly TimeSpan PendingLeaseDuration = TimeSpan.FromMinutes(15);

    public async Task<RegistrationIdempotencyAcquireResult> AcquireAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RegistrationIdempotencyRecord? existing = await context.Set<RegistrationIdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == RegistrationIdempotencyStatus.Completed)
                return RegistrationIdempotencyAcquireResult.AlreadyCompleted;

            if (existing.Status == RegistrationIdempotencyStatus.Pending)
            {
                if (DateTimeOffset.UtcNow - existing.UpdatedAt < PendingLeaseDuration)
                    return RegistrationIdempotencyAcquireResult.InProgress;

                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                return RegistrationIdempotencyAcquireResult.Acquired;
            }

            existing.Status = RegistrationIdempotencyStatus.Pending;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
            return RegistrationIdempotencyAcquireResult.Acquired;
        }

        context.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            Status = RegistrationIdempotencyStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return RegistrationIdempotencyAcquireResult.Acquired;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            return RegistrationIdempotencyAcquireResult.InProgress;
        }
    }

    public async Task MarkCompletedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RegistrationIdempotencyRecord? row = await context.Set<RegistrationIdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (row is null)
            return;

        row.Status = RegistrationIdempotencyStatus.Completed;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RegistrationIdempotencyRecord? row = await context.Set<RegistrationIdempotencyRecord>()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (row is null)
            return;

        row.Status = RegistrationIdempotencyStatus.Failed;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }
}
