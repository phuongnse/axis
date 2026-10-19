using Axis.Configuration.Storage;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The steps that bring one tenant database to the schema of this build: the grants, the EF Core migrations, the runtime
/// role check and the read of the schema version. <c>tenant create</c>, <c>migrate</c> and <c>activate</c> share them. Every step runs as
/// the tenant's schema role, except the role check, which runs as its runtime role. The schema connection opens at the
/// first step that needs it and stays open until <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class TenantDatabaseSteps(
    TenantSecretCipher cipher,
    TenantRow row,
    string host,
    int port,
    CancellationToken cancellationToken) : ITenantConnections
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

    public async Task GrantAsync()
    {
        var connection = await SchemaConnectionAsync();
        var database = EntityNaming.Quote(row.DatabaseName);
        var runtime = EntityNaming.Quote(row.RuntimeUser);
        // Without this, any login role of the server could connect to the tenant database.
        await using (var command = new NpgsqlCommand(
            $"REVOKE CONNECT, TEMPORARY ON DATABASE {database} FROM PUBLIC; GRANT CONNECT, TEMPORARY ON DATABASE {database} TO {runtime}",
            connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await RuntimeRoleGrants.ApplyAsync(connection, row.RuntimeUser, cancellationToken);
    }

    public async Task MigrateAsync()
    {
        var connection = await SchemaConnectionAsync();
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        await configuration.Database.MigrateAsync(cancellationToken);
        await data.Database.MigrateAsync(cancellationToken);
        await processes.Database.MigrateAsync(cancellationToken);
    }

    public async Task CheckRuntimeRoleAsync()
    {
        await using var connection = await OpenRuntimeConnectionAsync();
        await RuntimeRoleCheck.CheckAsync(connection, row.Id, cancellationToken);
    }

    /// <summary>A new open connection as the tenant's runtime role. The caller disposes it.</summary>
    public async Task<NpgsqlConnection> OpenRuntimeConnectionAsync()
    {
        var password = cipher.Decrypt(row.Id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal();
        var connection = new NpgsqlConnection(ConnectionString(row.RuntimeUser, password));
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    /// <summary>The newest migration applied to the tenant database.</summary>
    public async Task<string> ReadSchemaVersionAsync() =>
        await TenantSchemaVersion.ReadAsync(await SchemaConnectionAsync(), cancellationToken);

    /// <summary>A new open connection as the tenant's schema role. The caller disposes it.</summary>
    public async Task<NpgsqlConnection> OpenSchemaConnectionAsync()
    {
        var password = cipher.Decrypt(row.Id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var connection = new NpgsqlConnection(ConnectionString(row.SchemaUser, password));
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    public async Task<NpgsqlConnection> SchemaConnectionAsync()
    {
        if (_schemaConnection is null)
        {
            var password = cipher.Decrypt(row.Id, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
            var connection = new NpgsqlConnection(ConnectionString(row.SchemaUser, password));
            await connection.OpenAsync(cancellationToken);
            _schemaConnection = connection;
        }

        return _schemaConnection;
    }

    /// <summary>The text for a step's line. Never <c>ToString()</c>: an Npgsql message can name the host.</summary>
    public static string Safe(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        InvalidOperationException invalid => invalid.Message,
        PostgresException postgres => postgres.MessageText,
        _ => exception.GetType().Name,
    };

    private string ConnectionString(string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = row.DatabaseName,
            Username = user,
            Password = password,
            Pooling = false,
        }.ConnectionString;
}
