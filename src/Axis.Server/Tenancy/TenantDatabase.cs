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

    /// <summary>The scope's tenant connection, which the scope owns.</summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    public async ValueTask<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default) =>
        _connection ??= await connections.OpenConnectionAsync(cancellationToken);

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

    public async ValueTask DisposeAsync()
    {
        if (_configuration is not null)
        {
            await _configuration.DisposeAsync();
        }

        if (_data is not null)
        {
            await _data.DisposeAsync();
        }

        if (_processes is not null)
        {
            await _processes.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
