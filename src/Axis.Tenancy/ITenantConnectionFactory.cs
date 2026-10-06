using Npgsql;

namespace Axis.Tenancy;

/// <summary>Opens connections to the current tenant's database only.</summary>
public interface ITenantConnectionFactory
{
    /// <summary>Opens a connection to the database of the current tenant.</summary>
    /// <exception cref="InvalidOperationException">No tenant context is set.</exception>
    ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
