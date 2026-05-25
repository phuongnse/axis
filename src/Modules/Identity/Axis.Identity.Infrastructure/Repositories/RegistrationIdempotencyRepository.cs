using Axis.Identity.Application.Repositories;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Repositories;

internal sealed class RegistrationIdempotencyRepository(IdentityDbContext context)
    : IRegistrationIdempotencyRepository
{
    public async Task<bool> TryClaimAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        bool exists = await context.Set<RegistrationIdempotencyRecord>()
            .AnyAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (exists)
            return false;

        context.Set<RegistrationIdempotencyRecord>().Add(new RegistrationIdempotencyRecord
        {
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }
}
