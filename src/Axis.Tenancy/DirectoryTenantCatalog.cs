using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The tenant catalog read from the tenant directory, the <c>directory</c> source. It holds the active tenants
/// read at one moment. Each password and secret stays encrypted in memory and is decrypted only when a
/// connection opens, the sign-in client is set up or a binding is resolved.
/// </summary>
public sealed class DirectoryTenantCatalog : ITenantCatalog
{
    private readonly Dictionary<string, TenantEntry> _tenantsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TenantEntry> _tenantsByHost = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryTenantCatalog(IEnumerable<TenantEntry> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

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

    /// <summary>
    /// Reads the active tenants, their hosts and their bindings over <paramref name="platformConnectionString"/>. The
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
            tenants = await context.Tenants.AsNoTracking()
                .Where(tenant => tenant.Status == "active")
                .ToListAsync(cancellationToken);
            var ids = tenants.Select(tenant => tenant.Id).ToArray();
            hosts = await context.TenantHosts.AsNoTracking()
                .Where(host => ids.Contains(host.TenantId))
                .OrderBy(host => host.Host)
                .ToListAsync(cancellationToken);
            bindings = await context.TenantBindings.AsNoTracking()
                .Where(binding => ids.Contains(binding.TenantId))
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
        {
            throw new InvalidOperationException("The tenant directory could not be read over ConnectionStrings:Platform.", exception);
        }

        var problems = new List<string>();
        foreach (var tenant in tenants.OrderBy(tenant => tenant.Id, StringComparer.Ordinal))
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

        return new DirectoryTenantCatalog(tenants.Select(row => Entry(
            row,
            [.. hosts.Where(host => host.TenantId == row.Id).Select(host => host.Host)],
            bindings.Where(binding => binding.TenantId == row.Id),
            cipher)));
    }

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
