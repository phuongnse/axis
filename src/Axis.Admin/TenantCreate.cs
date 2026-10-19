using System.Security.Cryptography;
using Axis.Configuration.Storage;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant create</c>: writes the directory entry as <c>provisioning</c>, creates the database roles and the database,
/// grants and migrates it as the schema role, checks the runtime role and sets the entry <c>active</c>. A rerun on a
/// <c>provisioning</c> entry skips what is done. Each step writes one line that names the tenant and never a password,
/// a host or a connection string.
/// </summary>
internal sealed class TenantCreate : IAsyncDisposable
{
    private const string Provisioning = "provisioning";

    private readonly AdminSettings _settings;
    private readonly TenantCreateRequest _request;
    private readonly TextWriter _output;
    private readonly CancellationToken _cancellationToken;
    private readonly string _id;
    private readonly string _database;
    private readonly string _schemaRole;
    private readonly string _runtimeRole;
    private readonly PlatformDbContext _platform;
    private readonly NpgsqlConnectionStringBuilder _admin;

    private TenantRow? _row;
    private NpgsqlConnection? _adminConnection;
    private NpgsqlConnection? _schemaConnection;

    public TenantCreate(AdminSettings settings, TenantCreateRequest request, TextWriter output, CancellationToken cancellationToken)
    {
        _settings = settings;
        _request = request;
        _output = output;
        _cancellationToken = cancellationToken;
        _id = request.Id;
        _database = $"axis_{_id}";
        _schemaRole = $"axis_{_id}_schema";
        _runtimeRole = $"axis_{_id}_runtime";
        _admin = settings.Admin;
        _platform = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(settings.PlatformConnectionString).Options);
    }

    public async Task<int> RunAsync()
    {
        try
        {
            var entry = await EntryAsync();
            if (entry is not null)
            {
                return entry.Value;
            }

            if (await Step("roles", EnsureRolesAsync)
                && await Step("database", EnsureDatabaseAsync)
                && await Step("grants", GrantAsync)
                && await Step("migrations", MigrateAsync)
                && await Step("grants", GrantAsync)
                && await Step("role-check", CheckRuntimeRoleAsync)
                && await Step("version", ActivateAsync))
            {
                return 0;
            }

            return 1;
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_adminConnection is not null)
        {
            await _adminConnection.DisposeAsync();
            _adminConnection = null;
        }

        if (_schemaConnection is not null)
        {
            await _schemaConnection.DisposeAsync();
            _schemaConnection = null;
        }

        await _platform.DisposeAsync();
    }

    // Returns the exit code that ends the command, or null to go on.
    private async Task<int?> EntryAsync()
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var hosts = _request.Hosts
                .Select(host => host.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var keys = hosts.Select(host => host.ToLowerInvariant()).ToArray();

            _row = await _platform.Tenants.FirstOrDefaultAsync(tenant => tenant.Id == _id, _cancellationToken);
            if (_row is not null && _row.Status != Provisioning)
            {
                Failed("entry", "the tenant already exists");
                return 1;
            }

            var taken = await _platform.TenantHosts.AnyAsync(
                host => host.TenantId != _id && keys.Contains(host.Host.ToLower()),
                _cancellationToken);
            if (taken)
            {
                Failed("entry", "a host belongs to another tenant");
                return 2;
            }

            await using var transaction = await _platform.Database.BeginTransactionAsync(_cancellationToken);
            var secret = _settings.Cipher.Encrypt(_id, TenantSecretFields.OidcClientSecret, _settings.OidcClientSecret);
            if (_row is null)
            {
                _row = new TenantRow { Id = _id, Status = Provisioning };
                _platform.Tenants.Add(_row);
            }
            else
            {
                // A provisioning tenant is never served, so an operator can fix the hosts and the OIDC settings on a rerun.
                _row.Version++;
                _platform.TenantHosts.RemoveRange(_platform.TenantHosts.Where(host => host.TenantId == _id));
                await _platform.SaveChangesAsync(_cancellationToken);
            }

            _row.DatabaseHost = _admin.Host!;
            _row.DatabasePort = _admin.Port;
            _row.DatabaseName = _database;
            _row.SchemaUser = _schemaRole;
            _row.RuntimeUser = _runtimeRole;
            _row.OidcAuthority = _request.OidcAuthority;
            _row.OidcClientId = _request.OidcClientId;
            _row.OidcClientSecret = secret;
            _platform.TenantHosts.AddRange(hosts.Select(host => new TenantHostRow { TenantId = _id, Host = host }));
            await _platform.SaveChangesAsync(_cancellationToken);
            await transaction.CommitAsync(_cancellationToken);

            Ok("entry");
            return null;
        }
        catch (Exception exception) when (IsHostConflict(exception))
        {
            Failed("entry", "a host belongs to another tenant");
            return 2;
        }
        catch (Exception exception)
        {
            Failed("entry", Safe(exception));
            return 1;
        }
    }

    private static bool IsHostConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    private async Task EnsureRolesAsync()
    {
        await EnsureRoleAsync(_schemaRole, TenantSecretFields.SchemaPassword, row => row.SchemaPassword, (row, value) => row.SchemaPassword = value);
        await EnsureRoleAsync(_runtimeRole, TenantSecretFields.RuntimePassword, row => row.RuntimePassword, (row, value) => row.RuntimePassword = value);
    }

    private async Task EnsureRoleAsync(string role, string field, Func<TenantRow, byte[]> read, Action<TenantRow, byte[]> write)
    {
        var row = _row!;
        var admin = await AdminConnectionAsync();
        var exists = await ScalarAsync<bool>(admin, "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @name)", ("name", role));
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
            write(row, _settings.Cipher.Encrypt(_id, field, password));
            row.Version++;
            await _platform.SaveChangesAsync(_cancellationToken);
        }
        else if (!exists)
        {
            password = _settings.Cipher.Decrypt(_id, field, stored).Reveal();
        }
        else
        {
            return;
        }

        // The password is hex, so it needs no escaping. An existing role is never altered.
        await ExecuteAsync(admin, $"CREATE ROLE {EntityNaming.Quote(role)} LOGIN PASSWORD '{password}'");
    }

    private async Task EnsureDatabaseAsync()
    {
        var admin = await AdminConnectionAsync();
        var owner = await ScalarAsync<string?>(
            admin,
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
        await ExecuteAsync(admin, $"GRANT {EntityNaming.Quote(_schemaRole)} TO CURRENT_USER WITH INHERIT FALSE, SET TRUE");
        await ExecuteAsync(admin, $"CREATE DATABASE {EntityNaming.Quote(_database)} OWNER {EntityNaming.Quote(_schemaRole)}");
    }

    private async Task GrantAsync()
    {
        var connection = await SchemaConnectionAsync();
        var database = EntityNaming.Quote(_database);
        var runtime = EntityNaming.Quote(_runtimeRole);
        // Without this, any login role of the server could connect to the tenant database.
        await ExecuteAsync(
            connection,
            $"REVOKE CONNECT, TEMPORARY ON DATABASE {database} FROM PUBLIC; GRANT CONNECT, TEMPORARY ON DATABASE {database} TO {runtime}");
        await RuntimeRoleGrants.ApplyAsync(connection, _runtimeRole, _cancellationToken);
    }

    private async Task MigrateAsync()
    {
        var connection = await SchemaConnectionAsync();
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        await configuration.Database.MigrateAsync(_cancellationToken);
        await data.Database.MigrateAsync(_cancellationToken);
        await processes.Database.MigrateAsync(_cancellationToken);
    }

    private async Task CheckRuntimeRoleAsync()
    {
        var password = _settings.Cipher.Decrypt(_id, TenantSecretFields.RuntimePassword, _row!.RuntimePassword).Reveal();
        await using var connection = new NpgsqlConnection(TenantConnectionString(_runtimeRole, password));
        await connection.OpenAsync(_cancellationToken);
        await RuntimeRoleCheck.CheckAsync(connection, _id, _cancellationToken);
    }

    private async Task ActivateAsync()
    {
        var connection = await SchemaConnectionAsync();
        await using var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connection).Options);
        await using var data = new DataDbContext(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connection).Options);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
        var applied = (await configuration.Database.GetAppliedMigrationsAsync(_cancellationToken))
            .Concat(await data.Database.GetAppliedMigrationsAsync(_cancellationToken))
            .Concat(await processes.Database.GetAppliedMigrationsAsync(_cancellationToken));

        var row = _row!;
        row.SchemaVersion = applied.Max(StringComparer.Ordinal) ?? "";
        row.Status = "active";
        row.Version++;
        await _platform.SaveChangesAsync(_cancellationToken);
    }

    // Runs one step. Any failure, also a stop by Ctrl+C, writes a failed line and ends the command.
    private async Task<bool> Step(string name, Func<Task> work)
    {
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            await work();
        }
        catch (Exception exception)
        {
            Failed(name, Safe(exception));
            return false;
        }

        Ok(name);
        return true;
    }

    // Never ToString(): an Npgsql message can name the host.
    private static string Safe(Exception exception) => exception switch
    {
        OperationCanceledException => "canceled",
        InvalidOperationException invalid => invalid.Message,
        PostgresException postgres => postgres.MessageText,
        _ => exception.GetType().Name,
    };

    private void Ok(string step)
    {
        var line = $"{_id} {step} ok";
        _output.WriteLine(line);
    }

    private void Failed(string step, string message)
    {
        var line = $"{_id} {step} failed: {message}";
        _output.WriteLine(line);
    }

    private async Task<NpgsqlConnection> AdminConnectionAsync()
    {
        if (_adminConnection is null)
        {
            var connection = new NpgsqlConnection(_admin.ConnectionString);
            await connection.OpenAsync(_cancellationToken);
            _adminConnection = connection;
        }

        return _adminConnection;
    }

    private async Task<NpgsqlConnection> SchemaConnectionAsync()
    {
        if (_schemaConnection is null)
        {
            var password = _settings.Cipher.Decrypt(_id, TenantSecretFields.SchemaPassword, _row!.SchemaPassword).Reveal();
            var connection = new NpgsqlConnection(TenantConnectionString(_schemaRole, password));
            await connection.OpenAsync(_cancellationToken);
            _schemaConnection = connection;
        }

        return _schemaConnection;
    }

    private string TenantConnectionString(string user, string password) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = _admin.Host,
            Port = _admin.Port,
            Database = _database,
            Username = user,
            Password = password,
            Pooling = false,
        }.ConnectionString;

    private async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(_cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, (string Name, object Value) parameter)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        var value = await command.ExecuteScalarAsync(_cancellationToken);
        return value is null or DBNull ? default! : (T)value;
    }
}
