using Axis.Solutions.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

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

    public async Task AcquirePublisherFenceAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        IDbContextTransaction transaction = _transaction
            ?? throw new InvalidOperationException("A Solutions transaction is required for a publisher fence.");
        await using NpgsqlCommand command = ((NpgsqlConnection)context.Database.GetDbConnection()).CreateCommand();
        command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_advisory_xact_lock(hashtextextended(@publisher_id, 0))";
        command.Parameters.AddWithValue("publisher_id", NpgsqlDbType.Text, publisherId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException exception) { throw new SolutionPersistenceException("solutions.persistence.concurrent_update", exception); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" } postgres)
        {
            string problemCode = postgres.ConstraintName switch
            {
                "ux_solution_versions_identity" => "solutions.persistence.version_identity_conflict",
                "ux_solution_installations_workspace_solution" => "solutions.persistence.installation_solution_conflict",
                "ux_solution_operations_workspace_idempotency" => "solutions.persistence.operation_idempotency_conflict",
                _ => "solutions.persistence.unique_conflict",
            };
            throw new SolutionPersistenceException(problemCode, exception);
        }
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
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
        context.ChangeTracker.Clear();
    }
}
