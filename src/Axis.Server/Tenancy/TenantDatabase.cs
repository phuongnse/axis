using System.Globalization;
using Axis.Configuration.Storage;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.Tenancy;

/// <summary>
/// One tenant database connection per request scope, opened on first use and disposed with the
/// scope. The module contexts and raw commands share it, so activation and record commands can run
/// over the same connection. Requests that never touch the database open no connection.
/// </summary>
internal sealed class TenantDatabase(ITenantConnectionFactory connections) : IAsyncDisposable
{
    private NpgsqlConnection? _connection;
    private ConfigurationDbContext? _configuration;
    private DataDbContext? _data;
    private ProcessesDbContext? _processes;
    private TimeSpan? _readLimit;

    /// <summary>
    /// The read-only transaction that <see cref="LimitReads"/> asked for, once the scope's connection
    /// is open. It is <see langword="null"/> when no limit is set. The handlers of a limited request pass it
    /// to the helpers that take a transaction, so those can use savepoints. It always rolls back.
    /// </summary>
    public NpgsqlTransaction? ReadTransaction { get; private set; }

    /// <summary>
    /// Runs every statement of this scope in one read-only transaction whose <c>statement_timeout</c> is
    /// <paramref name="timeout"/>. The transaction begins on the first use of the connection, so a request
    /// that never touches the database still opens none. The limit is set for the transaction, so it never
    /// reaches the next user of the pooled connection. It does no I/O.
    /// </summary>
    public void LimitReads(TimeSpan timeout) => _readLimit = timeout;

    /// <summary>
    /// The <c>statement_timeout</c> that <see cref="LimitWrites"/> asked for, or <see langword="null"/> when
    /// this scope's writes run with no limit of their own.
    /// </summary>
    public TimeSpan? WriteLimit { get; private set; }

    /// <summary>
    /// Asks for each write transaction of this scope to run under a <c>statement_timeout</c> of
    /// <paramref name="timeout"/>, which <see cref="LimitWriteAsync"/> sets. It does no I/O.
    /// </summary>
    public void LimitWrites(TimeSpan timeout) => WriteLimit = timeout;

    /// <summary>
    /// Sets the write limit for <paramref name="transaction"/> only, so it never reaches the next user of the
    /// pooled connection. It does nothing when no limit is set. Call it first in the write transaction, before
    /// the start lock, so the limit also bounds a wait behind an activation.
    /// </summary>
    public Task LimitWriteAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
        WriteLimit is { } limit ? StatementTimeout.SetLocalAsync(transaction, limit, cancellationToken) : Task.CompletedTask;

    /// <summary>The scope's tenant connection, which the scope owns.</summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        _connection ??= await connections.OpenConnectionAsync(cancellationToken);
        if (_readLimit is { } limit && ReadTransaction is null)
        {
            var transaction = await _connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using var command = new NpgsqlCommand(
                    "SET TRANSACTION READ ONLY; SELECT set_config('statement_timeout', @milliseconds, true)", _connection, transaction);
                command.Parameters.AddWithValue("milliseconds", ((long)limit.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }

            ReadTransaction = transaction;
        }

        return _connection;
    }

    /// <summary>Rolls back and disposes the read transaction, if one began. Reads have nothing to keep.</summary>
    public async ValueTask EndReadsAsync()
    {
        if (ReadTransaction is { } transaction)
        {
            ReadTransaction = null;
            await transaction.DisposeAsync();
        }
    }

    public async ValueTask<ConfigurationDbContext> GetConfigurationAsync(CancellationToken cancellationToken = default) =>
        _configuration ??= new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(await GetConnectionAsync(cancellationToken))
            .Options);

    public async ValueTask<DataDbContext> GetDataAsync(CancellationToken cancellationToken = default) =>
        _data ??= new DataDbContext(new DbContextOptionsBuilder<DataDbContext>()
            .UseNpgsql(await GetConnectionAsync(cancellationToken))
            .Options);

    public async ValueTask<ProcessesDbContext> GetProcessesAsync(CancellationToken cancellationToken = default) =>
        _processes ??= new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>()
            .UseNpgsql(await GetConnectionAsync(cancellationToken))
            .Options);

    /// <summary>
    /// Disposes the scope's contexts and returns its connection to the pool. The next use opens a
    /// fresh pooled connection, so a request can hold none while it waits.
    /// </summary>
    public async ValueTask ReleaseConnectionAsync()
    {
        await EndReadsAsync();
        if (_configuration is not null)
        {
            await _configuration.DisposeAsync();
            _configuration = null;
        }

        if (_data is not null)
        {
            await _data.DisposeAsync();
            _data = null;
        }

        if (_processes is not null)
        {
            await _processes.DisposeAsync();
            _processes = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public ValueTask DisposeAsync() => ReleaseConnectionAsync();
}
