using System.Collections.Concurrent;
using Axis.Core.Tenancy;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// Keeps one <see cref="NpgsqlDataSource"/> per tenant, created on first use and disposed with the factory.
/// </summary>
public sealed class TenantConnectionFactory(TenantOptions options, ITenantContextAccessor accessor)
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
        new(() => options.Tenants.TryGetValue(tenantId, out var settings)
            ? NpgsqlDataSource.Create(settings.ConnectionString!)
            : throw new InvalidOperationException($"Tenant '{tenantId}' is not configured."));
}
