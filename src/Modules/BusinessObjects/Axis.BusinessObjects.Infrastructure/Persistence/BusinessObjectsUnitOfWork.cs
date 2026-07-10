using Axis.BusinessObjects.Application.Services;
using Axis.Shared.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.BusinessObjects.Infrastructure.Persistence;

internal sealed class BusinessObjectsUnitOfWork(BusinessObjectsDbContext context) : IUnitOfWork
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
                "An object definition with a conflicting unique key already exists.", ex);
        }
    }
}
