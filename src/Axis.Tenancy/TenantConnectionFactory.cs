using System.Collections.Concurrent;
using Axis.Core.Tenancy;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// Keeps one <see cref="NpgsqlDataSource"/> per tenant, created on first use and disposed with the factory.
/// </summary>
public sealed class TenantConnectionFactory(ITenantCatalog catalog, ITenantContextAccessor accessor)
    : ITenantConnectionFactory, IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> _dataSources = new(StringComparer.Ordinal);
    private bool _disposed;

    public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tenant = accessor.Current
            ?? throw new InvalidOperationException("No tenant context is set. Tenant database connections are only available while a tenant is resolved.");

        var dataSource = _dataSources.GetOrAdd(tenant.TenantId, CreateDataSource).Value;
        return await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops the tenant's connection pool, so the next connection opens a pool over the tenant's current settings.
    /// A request that opens a connection at this moment may fail once.
    /// </summary>
    public async ValueTask RetireAsync(string tenantId)
    {
        if (_dataSources.TryRemove(tenantId, out var dataSource) && dataSource.IsValueCreated)
        {
            await dataSource.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var dataSource in _dataSources.Values.Where(dataSource => dataSource.IsValueCreated))
        {
            await dataSource.Value.DisposeAsync().ConfigureAwait(false);
        }

        _dataSources.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var dataSource in _dataSources.Values.Where(dataSource => dataSource.IsValueCreated))
        {
            dataSource.Value.Dispose();
        }

        _dataSources.Clear();
    }

    private Lazy<NpgsqlDataSource> CreateDataSource(string tenantId) =>
        new(() => catalog.Find(tenantId) is { } tenant
            ? NpgsqlDataSource.Create(tenant.ConnectionString.Reveal())
            : throw new InvalidOperationException($"Tenant '{tenantId}' is not configured."));
}
