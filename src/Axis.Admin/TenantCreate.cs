using Axis.Processes.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Admin;

/// <summary>
/// <c>tenant create</c>: writes the directory entry as <c>provisioning</c>, creates the tenant's realm in the identity provider
/// (a main tenant only), creates the database roles and the database, grants and migrates it as the schema role, checks the
/// runtime role and sets the entry <c>active</c>. The realm step saves the three generated client secrets, encrypted, and the
/// tenant's <c>Oidc</c> block before it calls the identity provider. A rerun on a <c>provisioning</c> entry skips what is done
/// and reuses the secrets. Each step writes one line that names the tenant and never a password,
/// a host or a connection string.
/// </summary>
internal sealed class TenantCreate : IAsyncDisposable
{
    private const string Provisioning = "provisioning";

    private readonly CreateSettings _settings;
    private readonly TenantCreateRequest _request;
    private readonly TenantRealm? _realm;
    private readonly TenantAudit _audit;
    private readonly TextWriter _output;
    private readonly CancellationToken _cancellationToken;
    private readonly string _id;
    private readonly string _database;
    private readonly string _schemaRole;
    private readonly string _runtimeRole;
    private readonly PlatformDbContext _platform;
    private readonly NpgsqlConnectionStringBuilder _admin;

    private TenantRow? _row;
    private TenantProvisioning? _provisioning;
    private TenantDatabaseSteps? _steps;

    public TenantCreate(CreateSettings settings, TenantCreateRequest request, TenantRealm? realm, TenantAudit audit, TextWriter output, CancellationToken cancellationToken)
    {
        _settings = settings;
        _request = request;
        _realm = realm;
        _audit = audit;
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

            if ((_request.PreviewOf is not null || await Step("realm", RealmAsync))
                && await Step("roles", () => Roles.EnsureRolesAsync())
                && await Step("database", () => Roles.EnsureDatabaseAsync())
                && await Step("grants", () => Steps.GrantAsync())
                && await Step("migrations", () => Steps.MigrateAsync())
                && await Step("grants", () => Steps.GrantAsync())
                && await Step("role-check", () => Steps.CheckRuntimeRoleAsync())
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
        if (_provisioning is not null)
        {
            await _provisioning.DisposeAsync();
            _provisioning = null;
        }

        if (_steps is not null)
        {
            await _steps.DisposeAsync();
            _steps = null;
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

            if (!await _audit.WriteAsync(_id, _output, _cancellationToken))
            {
                return 1;
            }

            await using var transaction = await _platform.Database.BeginTransactionAsync(_cancellationToken);
            if (_row is null)
            {
                // A preview tenant has no sign-in settings of its own. A main tenant gets them in the realm step.
                _row = new TenantRow { Id = _id, Status = Provisioning, OidcAuthority = "", OidcClientId = "", OidcClientSecret = [], PlatformAccounts = true };
                _platform.Tenants.Add(_row);
            }
            else
            {
                // A provisioning tenant is never served, so an operator can fix the hosts, the main tenant and the bindings on a rerun.
                _row.Version++;
                _platform.TenantHosts.RemoveRange(_platform.TenantHosts.Where(host => host.TenantId == _id));
                _platform.TenantBindings.RemoveRange(_platform.TenantBindings.Where(binding => binding.TenantId == _id));
                await _platform.SaveChangesAsync(_cancellationToken);
            }

            _row.DatabaseHost = _admin.Host!;
            _row.DatabasePort = _admin.Port;
            _row.DatabaseName = _database;
            _row.SchemaUser = _schemaRole;
            _row.RuntimeUser = _runtimeRole;
            _row.PreviewOf = _request.PreviewOf;
            _platform.TenantHosts.AddRange(hosts.Select(host => new TenantHostRow { TenantId = _id, Host = host }));
            _platform.TenantBindings.AddRange(_request.Bindings.Select(binding => new TenantBindingRow
            {
                TenantId = _id,
                Name = binding.Key,
                Kind = DeploymentBindingKinds.Url,
                Url = binding.Value,
            }));
            await _platform.SaveChangesAsync(_cancellationToken);
            await transaction.CommitAsync(_cancellationToken);

            Ok("entry");
            return null;
        }
        catch (Exception exception) when (IsPreviewConflict(exception))
        {
            Failed("entry", "the main tenant already has a preview tenant");
            return 2;
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

    private static bool IsPreviewConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ix_tenants_preview_of" } };

    private static bool IsHostConflict(Exception exception) =>
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };

    // The steps of the database run with the host and port of the admin connection, where the database was created.
    private TenantProvisioning Roles =>
        _provisioning ??= new TenantProvisioning(_admin, _settings.Cipher, _platform, _row!, _cancellationToken);

    private TenantDatabaseSteps Steps =>
        _steps ??= new TenantDatabaseSteps(_settings.Cipher, _row!, _admin.Host!, _admin.Port, TenantDatabaseTls.From(_admin.ConnectionString), _cancellationToken);

    // Saves the secrets and the Oidc block before the realm exists, so a stop between the two never loses a secret.
    private Task RealmAsync() => _realm!.EnsureForTenantAsync(_platform, _row!, _settings.Cipher, _cancellationToken);

    private async Task ActivateAsync()
    {
        var row = _row!;
        row.SchemaVersion = await Steps.ReadSchemaVersionAsync();
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

    private static string Safe(Exception exception) => TenantDatabaseSteps.Safe(exception);

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
}
