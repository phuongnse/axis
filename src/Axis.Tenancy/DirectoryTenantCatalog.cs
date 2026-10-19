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
    private readonly IReadOnlyDictionary<string, string> _oidcKeys;

    public DirectoryTenantCatalog(IEnumerable<TenantEntry> tenants)
        : this(tenants, [], new Dictionary<string, string>())
    {
    }

    internal DirectoryTenantCatalog(
        IEnumerable<TenantEntry> tenants,
        IEnumerable<string> unavailableHosts,
        IReadOnlyDictionary<string, string> databaseKeys,
        IReadOnlyDictionary<string, string>? oidcKeys = null)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _databaseKeys = databaseKeys;
        _oidcKeys = oidcKeys ?? new Dictionary<string, string>();
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
            _oidcKeys.Where(pair => !tenantIds.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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
    /// The ids of the tenants of this catalog that <paramref name="previous"/> does not hold, or holds on other
    /// sign-in settings, in ordinal order. A tenant whose OIDC authority, client id, encrypted client secret,
    /// endpoint audience or claim names differ counts, and nothing is decrypted to compare.
    /// </summary>
    public IReadOnlyList<string> OidcChanges(DirectoryTenantCatalog previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        return [.. Tenants
            .Select(tenant => tenant.Id)
            .Where(id => !previous._oidcKeys.TryGetValue(id, out var before)
                || !_oidcKeys.TryGetValue(id, out var now)
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
        var activeIds = active.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        return new DirectoryTenantCatalog(
            active.Select(row => Entry(
                row,
                [.. hosts.Where(host => host.TenantId == row.Id).Select(host => host.Host)],
                bindings.Where(binding => binding.TenantId == row.Id),
                cipher)),
            hosts.Where(host => !activeIds.Contains(host.TenantId)).Select(host => host.Host),
            active.ToDictionary(row => row.Id, DatabaseKey, StringComparer.Ordinal),
            active.ToDictionary(row => row.Id, OidcKey, StringComparer.Ordinal));
    }

    private static bool IsActive(TenantRow row) => row.Status == "active";

    // Compares the stored encrypted password, so nothing is decrypted to tell whether the database settings changed.
    private static string DatabaseKey(TenantRow row) =>
        $"{row.DatabaseHost}\n{row.DatabasePort}\n{row.DatabaseName}\n{row.RuntimeUser}\n{Convert.ToBase64String(row.RuntimePassword)}";

    // Compares the stored encrypted client secret, so nothing is decrypted to tell whether the sign-in settings changed.
    private static string OidcKey(TenantRow row) =>
        string.Join('\n', row.OidcAuthority, row.OidcClientId, Convert.ToBase64String(row.OidcClientSecret), row.EndpointAudience, row.ClaimUserId, row.ClaimDisplayName, row.ClaimRoles);

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
