using Axis.Tenancy;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The connections of a tenant of the <c>Tenants</c> configuration section, as its schema and runtime connection strings give
/// them. The schema connection opens at the first call and stays open until <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class ConfiguredTenantConnections(TenantEntry entry) : ITenantConnections
{
    private NpgsqlConnection? _schemaConnection;

    public async ValueTask DisposeAsync()
    {
        if (_schemaConnection is not null)
        {
            await _schemaConnection.DisposeAsync();
            _schemaConnection = null;
        }
    }

    public async Task<NpgsqlConnection> SchemaConnectionAsync()
    {
        if (_schemaConnection is null)
        {
            _schemaConnection = await OpenAsync(entry.SchemaConnectionString!);
        }

        return _schemaConnection;
    }

    public Task<NpgsqlConnection> OpenRuntimeConnectionAsync() => OpenAsync(entry.ConnectionString);

    /// <summary>A new open connection with the given connection string, which never pools.</summary>
    internal static async Task<NpgsqlConnection> OpenAsync(SecretValue connectionString)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString.Reveal()) { Pooling = false }.ConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }
}
