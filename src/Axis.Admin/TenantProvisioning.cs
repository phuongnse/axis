using System.Security.Cryptography;
using Axis.Data.Naming;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// The steps that give a tenant its database roles and its database, as the admin role: <c>tenant create</c> and
/// <c>tenant restore</c> share them. A generated password is saved, encrypted, before its role exists, so a stop between the
/// two never loses it. Each step skips what is done. The admin connection opens at the first step and stays open until
/// <see cref="DisposeAsync"/>. The caller owns the platform context and the directory row.
/// </summary>
internal sealed class TenantProvisioning(
    NpgsqlConnectionStringBuilder admin,
    TenantSecretCipher cipher,
    PlatformDbContext platform,
    TenantRow row,
    CancellationToken cancellationToken) : IAsyncDisposable
{
    private readonly string _schemaRole = $"axis_{row.Id}_schema";
    private readonly string _runtimeRole = $"axis_{row.Id}_runtime";
    private readonly string _database = $"axis_{row.Id}";

    private NpgsqlConnection? _adminConnection;

    public async ValueTask DisposeAsync()
    {
        if (_adminConnection is not null)
        {
            await _adminConnection.DisposeAsync();
            _adminConnection = null;
        }
    }

    public async Task EnsureRolesAsync()
    {
        await EnsureRoleAsync(_schemaRole, TenantSecretFields.SchemaPassword, tenant => tenant.SchemaPassword, (tenant, value) => tenant.SchemaPassword = value);
        await EnsureRoleAsync(_runtimeRole, TenantSecretFields.RuntimePassword, tenant => tenant.RuntimePassword, (tenant, value) => tenant.RuntimePassword = value);
    }

    public async Task EnsureDatabaseAsync()
    {
        var connection = await AdminConnectionAsync();
        var owner = await ScalarAsync<string?>(
            connection,
            "SELECT pg_get_userbyid(datdba)::text FROM pg_database WHERE datname = @name",
            ("name", _database));
        if (owner is not null)
        {
            if (owner != _schemaRole)
            {
                throw new InvalidOperationException($"The database '{_database}' exists and is not owned by the role '{_schemaRole}'.");
            }

            return;
        }

        // PostgreSQL 16 and later let a non-superuser create a database for a role only when it can SET ROLE to it.
        await ExecuteAsync(connection, $"GRANT {EntityNaming.Quote(_schemaRole)} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE");
        await ExecuteAsync(connection, $"CREATE DATABASE {EntityNaming.Quote(_database)} OWNER {EntityNaming.Quote(_schemaRole)}");
    }

    private async Task EnsureRoleAsync(string role, string field, Func<TenantRow, byte[]> read, Action<TenantRow, byte[]> write)
    {
        var connection = await AdminConnectionAsync();
        var exists = await ScalarAsync<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)", ("name", role));
        var stored = read(row);

        string password;
        if (stored.Length == 0)
        {
            if (exists)
            {
                throw new InvalidOperationException($"The database role '{role}' exists and is not recorded for this tenant.");
            }

            password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            // Save the password before the role exists, so a stop between the two never loses it.
            write(row, cipher.Encrypt(row.Id, field, password));
            row.Version++;
            await platform.SaveChangesAsync(cancellationToken);
        }
        else if (!exists)
        {
            password = cipher.Decrypt(row.Id, field, stored).Reveal();
        }
        else
        {
            return;
        }

        // The password is hex, so it needs no escaping. An existing role is never altered.
        await ExecuteAsync(connection, $"CREATE ROLE {EntityNaming.Quote(role)} LOGIN PASSWORD '{password}'");
    }

    private async Task<NpgsqlConnection> AdminConnectionAsync()
    {
        if (_adminConnection is null)
        {
            var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            _adminConnection = connection;
        }

        return _adminConnection;
    }

    private async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, (string Name, object Value) parameter)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? default! : (T)value;
    }
}
