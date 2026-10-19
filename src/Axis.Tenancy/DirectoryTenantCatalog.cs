using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The tenant catalog read from the tenant directory, the <c>directory</c> source. It holds the active tenants
/// read at one moment, and the hosts of the other tenants, which resolve no tenant but are known. Each password and secret stays encrypted in memory and is decrypted only when a
/// connection opens, the sign-in client is set up or a binding is resolved.
/// </summary>
public sealed class DirectoryTenantCatalog : ITenantCatalog
{
    private readonly Dictionary<string, TenantEntry> _tenantsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TenantEntry> _tenantsByHost = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _unavailableHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _databaseKeys;
    private readonly IReadOnlyDictionary<string, string> _schemaVersions;

    public DirectoryTenantCatalog(IEnumerable<TenantEntry> tenants)
        : this(tenants, [], new Dictionary<string, string>(), new Dictionary<string, string>())
    {
    }

    internal DirectoryTenantCatalog(
        IEnumerable<TenantEntry> tenants,
        IEnumerable<string> unavailableHosts,
        IReadOnlyDictionary<string, string> databaseKeys,
        IReadOnlyDictionary<string, string> schemaVersions)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _databaseKeys = databaseKeys;
        _schemaVersions = schemaVersions;
        _unavailableHosts.UnionWith(unavailableHosts);

        Tenants = [.. tenants.OrderBy(tenant => tenant.Id, StringComparer.Ordinal)];
        foreach (var tenant in Tenants)
        {
            _tenantsById.Add(tenant.Id, tenant);
            foreach (var host in tenant.Hosts)
            {
                _tenantsByHost[host] = tenant;
            }
        }
    }

    public IReadOnlyList<TenantEntry> Tenants { get; }

    public TenantEntry? Find(string tenantId) => _tenantsById.GetValueOrDefault(tenantId);

    public TenantEntry? FindByHost(string host) => _tenantsByHost.GetValueOrDefault(host);

    /// <summary>Whether the host belongs to a tenant of the directory that is not active, or was withheld.</summary>
    public bool IsUnavailableHost(string host) => _unavailableHosts.Contains(host);

    /// <summary>
    /// Returns a catalog without the tenants of <paramref name="tenantIds"/>. Their hosts become unavailable, so they
    /// answer that the tenant is not available.
    /// </summary>
    public DirectoryTenantCatalog Withhold(IReadOnlyCollection<string> tenantIds)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        var withheld = Tenants.Where(tenant => tenantIds.Contains(tenant.Id, StringComparer.Ordinal)).ToList();
        return new DirectoryTenantCatalog(
            Tenants.Except(withheld),
            _unavailableHosts.Concat(withheld.SelectMany(tenant => tenant.Hosts)),
            _databaseKeys.Where(pair => !tenantIds.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            _schemaVersions.Where(pair => !tenantIds.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// The ids of the tenants of this catalog whose recorded schema version is not <paramref name="schemaVersion"/>, in
    /// ordinal order. A host serves only the tenants on its own schema version.
    /// </summary>
    public IReadOnlyList<string> OnOtherSchemaVersion(string schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(schemaVersion);

        return [.. Tenants
            .Select(tenant => tenant.Id)
            .Where(id => !_schemaVersions.TryGetValue(id, out var version) || !string.Equals(version, schemaVersion, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Reads the recorded schema version of every active tenant, keyed by tenant id. It reads two columns and no secret.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The directory cannot be read. The message names <c>ConnectionStrings:Platform</c> and holds no connection value.
    /// </exception>
    public static async Task<IReadOnlyDictionary<string, string>> ReadSchemaVersionsAsync(
        string platformConnectionString,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);

        try
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options;
            await using var context = new PlatformDbContext(options);
            var rows = await context.Tenants.AsNoTracking()
                .Where(tenant => tenant.Status == "active")
                .Select(tenant => new { tenant.Id, tenant.SchemaVersion })
                .ToListAsync(cancellationToken);
            return rows.ToDictionary(row => row.Id, row => row.SchemaVersion, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new InvalidOperationException("The tenant directory could not be read over ConnectionStrings:Platform.", exception);
        }
    }

    /// <summary>
    /// The ids of the tenants of this catalog that <paramref name="previous"/> does not hold, or holds on other
    /// database settings, in ordinal order. A tenant whose database host, port, name, runtime user or encrypted
    /// runtime password differs counts, and nothing is decrypted to compare.
    /// </summary>
    public IReadOnlyList<string> DatabaseChanges(DirectoryTenantCatalog previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return [.. Tenants
            .Select(tenant => tenant.Id)
            .Where(id => !previous._databaseKeys.TryGetValue(id, out var before)
                || !_databaseKeys.TryGetValue(id, out var now)
                || !string.Equals(before, now, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Reads the tenants, their hosts and the bindings of the active tenants over <paramref name="platformConnectionString"/>. The
    /// catalog has no schema connection, because under the <c>directory</c> source the server holds only the runtime
    /// database role.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The directory cannot be read. The message names <c>ConnectionStrings:Platform</c> and holds no connection value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A binding is invalid, with the checks of the <c>configuration</c> source. The message names the tenant and the
    /// binding, never the value.
    /// </exception>
    public static async Task<DirectoryTenantCatalog> LoadAsync(
        string platformConnectionString,
        TenantSecretCipher cipher,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(cipher);

        List<TenantRow> tenants;
        List<TenantHostRow> hosts;
        List<TenantBindingRow> bindings;
        try
        {
            var options = new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platformConnectionString).Options;
            await using var context = new PlatformDbContext(options);
            tenants = await context.Tenants.AsNoTracking().ToListAsync(cancellationToken);
            hosts = await context.TenantHosts.AsNoTracking()
                .OrderBy(host => host.Host)
                .ToListAsync(cancellationToken);
            var ids = tenants.Where(IsActive).Select(tenant => tenant.Id).ToArray();
            bindings = await context.TenantBindings.AsNoTracking()
                .Where(binding => ids.Contains(binding.TenantId))
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new InvalidOperationException("The tenant directory could not be read over ConnectionStrings:Platform.", exception);
        }

        var active = tenants.Where(IsActive).ToList();
        var problems = new List<string>();
        foreach (var tenant in active.OrderBy(tenant => tenant.Id, StringComparer.Ordinal))
        {
            foreach (var binding in bindings.Where(binding => binding.TenantId == tenant.Id).OrderBy(binding => binding.Name, StringComparer.Ordinal))
            {
                // A secret is never decrypted here: it stays encrypted until the moment of use.
                var hasValue = binding.Kind == DeploymentBindingKinds.Secret
                    ? binding.SecretValue is { Length: > 0 }
                    : !string.IsNullOrWhiteSpace(binding.Url);
                TenantOptionsValidator.AddBindingProblems(tenant.Id, binding.Name, binding.Kind, hasValue, binding.Url, problems);
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The tenant directory is invalid. {string.Join(" ", problems)}");
        }

        var activeIds = active.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        return new DirectoryTenantCatalog(
            active.Select(row => Entry(
                row,
                [.. hosts.Where(host => host.TenantId == row.Id).Select(host => host.Host)],
                bindings.Where(binding => binding.TenantId == row.Id),
                cipher)),
            hosts.Where(host => !activeIds.Contains(host.TenantId)).Select(host => host.Host),
            active.ToDictionary(row => row.Id, DatabaseKey, StringComparer.Ordinal),
            active.ToDictionary(row => row.Id, row => row.SchemaVersion, StringComparer.Ordinal));
    }

    private static bool IsActive(TenantRow row) => row.Status == "active";

    // Compares the stored encrypted password, so nothing is decrypted to tell whether the database settings changed.
    private static string DatabaseKey(TenantRow row) =>
        $"{row.DatabaseHost}\n{row.DatabasePort}\n{row.DatabaseName}\n{row.RuntimeUser}\n{Convert.ToBase64String(row.RuntimePassword)}";

    private static TenantEntry Entry(TenantRow row, IReadOnlyList<string> hosts, IEnumerable<TenantBindingRow> bindingRows, TenantSecretCipher cipher)
    {
        var id = row.Id;
        var bindings = new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindingRows)
        {
            bindings[binding.Name] = binding.Kind == DeploymentBindingKinds.Secret
                ? new TenantBinding(
                    DeploymentBindingKinds.Secret,
                    null,
                    SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.Binding(binding.Name), binding.SecretValue ?? []).Reveal()))
                : new TenantBinding(binding.Kind, binding.Url, null);
        }

        var connectionString = SecretValue.Deferred(() => new NpgsqlConnectionStringBuilder
        {
            Host = row.DatabaseHost,
            Port = row.DatabasePort,
            Database = row.DatabaseName,
            Username = row.RuntimeUser,
            Password = cipher.Decrypt(id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal(),
        }.ConnectionString);

        var oidc = new TenantOidc(
            Blank(row.OidcAuthority),
            Blank(row.OidcClientId),
            row.OidcClientSecret.Length == 0
                ? null
                : SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.OidcClientSecret, row.OidcClientSecret).Reveal()),
            Blank(row.EndpointAudience),
            Blank(row.ClaimUserId),
            Blank(row.ClaimDisplayName),
            Blank(row.ClaimRoles));

        return new TenantEntry(id, hosts, connectionString, SchemaConnectionString: null, oidc, bindings);
    }

    // A blank value counts as missing.
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
