using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Shared.Infrastructure.Workspaces;

/// <summary>
/// Shared steps for per-module workspace schema provisioning. Each module supplies its own DbContext factory.
/// </summary>
public static class WorkspaceSchemaProvisioner
{
    private const int MaxAdvisoryLockAttempts = 30;
    private static readonly TimeSpan AdvisoryLockRetryDelay = TimeSpan.FromMilliseconds(100);

    public static async Task ProvisionAsync(
        string connectionString,
        string advisoryLockKey,
        Guid workspaceId,
        Func<CancellationToken, Task> migrateWorkspaceSchemaAsync,
        ILogger logger,
        string moduleName,
        CancellationToken cancellationToken)
    {
        string schema = $"workspace_{workspaceId:N}";

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await AcquireAdvisoryLockAsync(connection, advisoryLockKey, moduleName, cancellationToken);

        await using NpgsqlCommand createSchema = connection.CreateCommand();
        createSchema.CommandText = $"""CREATE SCHEMA IF NOT EXISTS "{schema}";""";
        await createSchema.ExecuteNonQueryAsync(cancellationToken);

        await migrateWorkspaceSchemaAsync(cancellationToken);

        logger.LogInformation(
            "{Module} workspace schema {Schema} provisioned",
            moduleName,
            schema);
    }

    private static async Task AcquireAdvisoryLockAsync(
        NpgsqlConnection connection,
        string advisoryLockKey,
        string moduleName,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxAdvisoryLockAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using NpgsqlCommand tryLock = connection.CreateCommand();
            tryLock.CommandText = "SELECT pg_try_advisory_lock(hashtextextended(@key, 0));";
            tryLock.Parameters.AddWithValue("key", advisoryLockKey);
            object? result = await tryLock.ExecuteScalarAsync(cancellationToken);
            if (result is bool acquired && acquired)
                return;

            if (attempt < MaxAdvisoryLockAttempts)
                await Task.Delay(AdvisoryLockRetryDelay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Could not acquire workspace provisioning advisory lock for module '{moduleName}' after {MaxAdvisoryLockAttempts} attempts.");
    }

    public static async Task MigrateWithFixedWorkspaceAsync<TContext>(
        string connectionString,
        Guid workspaceId,
        Func<DbContextOptions<TContext>, TContext> createContext,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        DbContextOptionsBuilder<TContext> optionsBuilder = new();
        optionsBuilder.UseNpgsql(connectionString);
        await using TContext context = createContext(optionsBuilder.Options);
        await context.Database.MigrateAsync(cancellationToken);
    }
}
