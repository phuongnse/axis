using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Axis.Server.Studio;

/// <summary>
/// The lock that every Studio write and the publish snapshot hold while they read a folder and, for a write, change it.
/// </summary>
internal interface IStudioWriteLock
{
    /// <summary>
    /// Waits for the lock of <paramref name="folder"/> and returns the handle that holds it. Disposing the handle
    /// releases the lock. Throws <see cref="StudioWriteLockUnavailableException"/> when the lock cannot be taken.
    /// </summary>
    Task<IAsyncDisposable> TakeAsync(string folder, CancellationToken cancellationToken);
}

/// <summary>The lock could not be taken: the wait timed out or the platform database could not be reached.</summary>
internal sealed class StudioWriteLockUnavailableException(Exception inner)
    : Exception("The Studio write lock could not be taken.", inner);

/// <summary>
/// A PostgreSQL transaction advisory lock in the platform database, keyed by the application folder. Server replicas
/// that share a folder share the lock, so one write at a time checks a version and writes the file. A create checks
/// that its id is unused in the whole folder, so the key is the folder and not the file. The lock waits up to
/// ten seconds. Ending the transaction, or losing the connection, releases it.
/// </summary>
internal sealed partial class PlatformStudioWriteLock(NpgsqlDataSource platform, ILogger<PlatformStudioWriteLock> logger) : IStudioWriteLock
{
    /// <summary>
    /// The advisory lock key of <paramref name="folder"/>: the first eight bytes of a SHA-256 hash of its full path.
    /// A collision with another advisory lock only makes a write wait. It never lets one through.
    /// </summary>
    public static long Key(string folder) =>
        BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
            Encoding.UTF8.GetBytes("axis.studio.folder\0" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))));

    public async Task<IAsyncDisposable> TakeAsync(string folder, CancellationToken cancellationToken)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        try
        {
            connection = await platform.OpenConnectionAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '10s'", connection, transaction))
            {
                await timeout.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
            command.Parameters.AddWithValue("key", Key(folder));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new Held(connection, transaction);
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }

            if (exception is not NpgsqlException)
            {
                throw;
            }

            LogUnavailable(exception.GetType().Name);
            throw new StudioWriteLockUnavailableException(exception);
        }
    }

    [LoggerMessage(LogLevel.Warning, "A Studio write lock could not be taken over ConnectionStrings:Platform ({Cause}). The write is refused with 503.")]
    private partial void LogUnavailable(string cause);

    // Disposing rolls the transaction back, which releases the lock, then returns the connection to the pool.
    private sealed class Held(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await transaction.DisposeAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
