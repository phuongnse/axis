using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The two connections that the release steps of one tenant use. The schema connection opens at the first call and stays open
/// until <see cref="IAsyncDisposable.DisposeAsync"/>. Each runtime connection is new, and its caller disposes it.
/// </summary>
internal interface ITenantConnections : IAsyncDisposable
{
    /// <summary>The open connection as the tenant's schema role.</summary>
    Task<NpgsqlConnection> SchemaConnectionAsync();

    /// <summary>A new open connection as the tenant's runtime role. The caller disposes it.</summary>
    Task<NpgsqlConnection> OpenRuntimeConnectionAsync();
}
