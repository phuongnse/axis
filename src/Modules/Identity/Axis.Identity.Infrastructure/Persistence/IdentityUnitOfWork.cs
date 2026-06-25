using Axis.Identity.Application.Services;
using Axis.Shared.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Identity.Infrastructure.Persistence;

internal sealed class IdentityUnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await context.SaveChangesAsync(ct);
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
    }
}
