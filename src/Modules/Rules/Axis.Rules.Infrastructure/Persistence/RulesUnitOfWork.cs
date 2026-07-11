using Axis.Rules.Application.Services;
using Axis.Shared.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Rules.Infrastructure.Persistence;

internal sealed class RulesUnitOfWork(RulesDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres && postgres.SqlState == "23505")
        {
            throw new UniqueConstraintException(
                "A rule definition with a conflicting unique key already exists.",
                exception);
        }
    }
}
