using Axis.Solutions.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Axis.Solutions.Infrastructure.Persistence;

internal sealed class SolutionsUnitOfWork(SolutionsDbContext context) : ISolutionsUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("A Solutions transaction is already active.");
        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException exception) { throw new SolutionPersistenceException("solutions.persistence.concurrent_update", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" }) { throw new SolutionPersistenceException("solutions.persistence.unique_conflict", exception); }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction = _transaction
            ?? throw new InvalidOperationException("No Solutions transaction is active.");
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
            return;
        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        context.ChangeTracker.Clear();
    }
}
