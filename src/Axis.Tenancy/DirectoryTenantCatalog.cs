using System.Text.Json;
using Axis.Configuration.Model;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The tenant catalog read from the tenant directory, the <c>directory</c> source. It holds the active tenants
/// read at one moment, and the hosts of the other tenants, which resolve no tenant but are known. Each password and secret stays encrypted in memory and is decrypted only when a
/// connection opens, the sign-in client is set up, a binding is resolved, an email is sent or the realm is synced.
/// </summary>
public sealed class DirectoryTenantCatalog : ITenantCatalog
{
    private readonly Dictionary<string, TenantEntry> _tenantsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TenantEntry> _tenantsByHost = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _unavailableHosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, string> _databaseKeys;
    private readonly IReadOnlyDictionary<string, string> _schemaVersions;
    private readonly IReadOnlyDictionary<string, string> _oidcKeys;

    public DirectoryTenantCatalog(IEnumerable<TenantEntry> tenants)
        : this(tenants, [], new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>())
    {
    }

    internal DirectoryTenantCatalog(
        IEnumerable<TenantEntry> tenants,
        IEnumerable<string> unavailableHosts,
        IReadOnlyDictionary<string, string> databaseKeys,
        IReadOnlyDictionary<string, string> schemaVersions,
        IReadOnlyDictionary<string, string>? oidcKeys = null)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        _databaseKeys = databaseKeys;
        _schemaVersions = schemaVersions;
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
            _schemaVersions.Where(pair => !tenantIds.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            _oidcKeys.Where(pair => !tenantIds.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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
    /// The ids of the tenants of this catalog that <paramref name="previous"/> does not hold, or holds on other
    /// sign-in settings, in ordinal order. A tenant whose OIDC authority, client id, encrypted client secret,
    /// endpoint audience, claim names, sign-in profile, encrypted client keys or platform sign-in differ counts, and nothing is decrypted to compare.
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
    /// <exception cref="InvalidOperationException">
    /// A binding is invalid, with the checks of the <c>configuration</c> source. The message names the tenant and the
    /// binding, never the value.
    /// </exception>
    public static Task<DirectoryTenantCatalog> LoadAsync(
        string platformConnectionString,
        TenantSecretCipher cipher,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        return LoadAsync(platformConnectionString, TenantDatabaseTls.From(platformConnectionString), cipher, cancellationToken);
    }

    // The tenant connections carry the TLS settings of the platform connection string, which the test PostgreSQL
    // cannot serve, so a test passes them apart from the string that reads the directory.
    internal static async Task<DirectoryTenantCatalog> LoadAsync(
        string platformConnectionString,
        TenantDatabaseTls tls,
        TenantSecretCipher cipher,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platformConnectionString);
        ArgumentNullException.ThrowIfNull(tls);
        ArgumentNullException.ThrowIfNull(cipher);

        List<TenantRow> tenants;
        List<TenantHostRow> hosts;
        List<TenantBindingRow> bindings;
        List<TenantPlatformProviderRow> platformProviders;
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
            platformProviders = await context.TenantPlatformProviders.AsNoTracking().ToListAsync(cancellationToken);
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
        var byId = tenants.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return new DirectoryTenantCatalog(
            active.Select(row => Entry(
                row,
                [.. hosts.Where(host => host.TenantId == row.Id)],
                bindings.Where(binding => binding.TenantId == row.Id),
                platformProviders.Where(provider => provider.TenantId == (row.PreviewOf ?? row.Id)),
                cipher,
                tls,
                row.PreviewOf is null ? null : byId[row.PreviewOf],
                row.PreviewOf is null ? PlatformSignInProviders.From(platformProviders.Where(provider => provider.TenantId == row.Id)) : null)),
            hosts.Where(host => !activeIds.Contains(host.TenantId)).Select(host => host.Host),
            active.ToDictionary(row => row.Id, DatabaseKey, StringComparer.Ordinal),
            active.ToDictionary(row => row.Id, row => row.SchemaVersion, StringComparer.Ordinal),
            active.ToDictionary(row => row.Id, row => OidcKey(
                row.PreviewOf is null ? row : byId[row.PreviewOf],
                row.PreviewOf is not null,
                platformProviders.Where(provider => provider.TenantId == (row.PreviewOf ?? row.Id))), StringComparer.Ordinal));
    }

    private static bool IsActive(TenantRow row) => row.Status == "active";

    // Compares the stored encrypted password, so nothing is decrypted to tell whether the database settings changed.
    private static string DatabaseKey(TenantRow row) =>
        $"{row.DatabaseHost}\n{row.DatabasePort}\n{row.DatabaseName}\n{row.RuntimeUser}\n{Convert.ToBase64String(row.RuntimePassword)}";

    // Compares the stored encrypted client secret and client keys, so nothing is decrypted to tell whether the sign-in
    // settings changed. The platform sign-in counts, so `tenant sign-in set` rebuilds the scheme's options at the next refresh.
    // A preview tenant is keyed by the settings of its main tenant, without the endpoint and service audiences it never
    // takes, so a change to the main tenant's sign-in reaches the preview at the next refresh.
    private static string OidcKey(TenantRow signIn, bool preview, IEnumerable<TenantPlatformProviderRow> platformProviders) =>
        string.Join(
            '\n',
            signIn.OidcAuthority,
            signIn.OidcClientId,
            Convert.ToBase64String(signIn.OidcClientSecret),
            preview ? null : signIn.EndpointAudience,
            preview ? null : signIn.ServiceAudience,
            signIn.ClaimUserId,
            signIn.ClaimDisplayName,
            signIn.ClaimRoles,
            signIn.ClaimEmail,
            signIn.ClaimLocale,
            signIn.OidcProfile,
            signIn.OidcClientKeyId,
            signIn.OidcClientKey is null ? null : Convert.ToBase64String(signIn.OidcClientKey),
            signIn.OidcPreviousClientKeyId,
            signIn.OidcPreviousClientKey is null ? null : Convert.ToBase64String(signIn.OidcPreviousClientKey),
            signIn.PlatformAccounts,
            string.Join(
                '\n',
                PlatformSignInOf(signIn, platformProviders)?.Providers.Select(provider => string.Join(
                    '\t',
                    provider.Name,
                    provider.Alias,
                    provider.RolesClaim,
                    string.Join(',', provider.RoleValues.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))))
                    ?? []));

    /// <summary>
    /// The platform sign-in of a tenant row with the rows of its providers, or <c>null</c> when none is stored: accounts
    /// off and no provider. An <c>oidc</c> provider's alias is <see cref="RealmSync.ProviderAliasPrefix"/> and the lower-case
    /// issuer binding, and a <c>saml</c> provider's is <see cref="RealmSync.SamlProviderAliasPrefix"/> and the lower-case metadata binding, as the realm names them. The providers are in ordinal order of name.
    /// </summary>
    internal static PlatformSignIn? PlatformSignInOf(TenantRow signIn, IEnumerable<TenantPlatformProviderRow> providers)
    {
        var rows = providers.OrderBy(provider => provider.Name, StringComparer.Ordinal).ToList();
        if (!signIn.PlatformAccounts && rows.Count == 0)
        {
            return null;
        }

        return new PlatformSignIn(
            signIn.PlatformAccounts,
            [.. rows.Select(provider => new PlatformSignInProvider(
                provider.Name,
                provider.Type switch
                {
                    "oidc" when Blank(provider.IssuerBinding) is { } issuer => RealmSync.ProviderAliasPrefix + issuer.ToLowerInvariant(),
                    "saml" when Blank(provider.MetadataBinding) is { } metadata => RealmSync.SamlProviderAliasPrefix + metadata.ToLowerInvariant(),
                    _ => null,
                },
                Blank(provider.RolesClaim),
                RoleValues(provider)))]);
    }

    private static Dictionary<string, string> RoleValues(TenantPlatformProviderRow provider)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(provider.RoleValues) ?? [];
        }
        catch (JsonException)
        {
            // Nothing maps, so the provider's people get no platform role.
            return [];
        }
    }

    // A preview tenant signs people in with the sign-in settings of its main tenant, whatever the main tenant's status,
    // and decrypts the client secret under the main tenant's id. It never takes the main tenant's EndpointAudience or
    // ServiceAudience, so it accepts no machine-caller or service account bearer token. Its bindings are only its own. It also
    // takes the main tenant's platform sign-in, whose provider rows the caller passes.
    private static TenantEntry Entry(
        TenantRow row,
        IReadOnlyList<TenantHostRow> hostRows,
        IEnumerable<TenantBindingRow> bindingRows,
        IEnumerable<TenantPlatformProviderRow> platformProviders,
        TenantSecretCipher cipher,
        TenantDatabaseTls tls,
        TenantRow? main,
        IReadOnlyList<IdentityProviderModel>? realmProviders)
    {
        var id = row.Id;
        var bindings = Bindings(id, bindingRows, cipher);

        var connectionString = SecretValue.Deferred(() =>
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = row.DatabaseHost,
                Port = row.DatabasePort,
                Database = row.DatabaseName,
                Username = row.RuntimeUser,
                Password = cipher.Decrypt(id, TenantSecretFields.RuntimePassword, row.RuntimePassword).Reveal(),
            };
            tls.ApplyTo(builder);
            return builder.ConnectionString;
        });

        var signIn = main ?? row;
        var oidc = new TenantOidc(
            Blank(signIn.OidcAuthority),
            Blank(signIn.OidcClientId),
            signIn.OidcClientSecret.Length == 0
                ? null
                : SecretValue.Deferred(() => cipher.Decrypt(signIn.Id, TenantSecretFields.OidcClientSecret, signIn.OidcClientSecret).Reveal()),
            main is null ? Blank(signIn.EndpointAudience) : null,
            main is null ? Blank(signIn.ServiceAudience) : null,
            Blank(signIn.ClaimUserId),
            Blank(signIn.ClaimDisplayName),
            Blank(signIn.ClaimRoles),
            Blank(signIn.ClaimEmail),
            Blank(signIn.ClaimLocale),
            signIn.OidcProfile,
            ClientKeys(signIn, cipher));

        var hostSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hostRows.Where(host => host.SitePath is not null))
        {
            hostSites[host.Host] = host.SitePath!;
        }

        return new TenantEntry(
            id,
            [.. hostRows.Select(host => host.Host)],
            connectionString,
            SchemaConnectionString: null,
            oidc,
            bindings,
            row.PreviewOf,
            Email(row, cipher),
            Realm(row, cipher),
            RealmUsers(row, cipher),
            PlatformSignInOf(signIn, platformProviders),
            realmProviders,
            hostSites);
    }

    /// <summary>
    /// The realm details of a tenant row, or <c>null</c> when the row has none: a preview tenant, a row without an
    /// activation client secret, or a row whose <c>Oidc</c> authority does not end in <c>realms/</c> and the tenant id,
    /// such as one that signs in with an outside identity provider. The realm name is the tenant id. The secret stays
    /// encrypted until it is revealed.
    /// </summary>
    public static TenantRealmAccess? Realm(TenantRow row, TenantSecretCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cipher);

        var id = row.Id;
        var stored = row.RealmActivationClientSecret;
        if (row.PreviewOf is not null || stored is not { Length: > 0 } || TenantRealmAddress.BaseOf(row.OidcAuthority, id) is not { } baseAddress)
        {
            return null;
        }

        return new TenantRealmAccess(
            baseAddress,
            id,
            SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.RealmActivationClientSecret, stored).Reveal()),
            row.OidcClientSecret is { Length: > 0 } platform
                ? SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.OidcClientSecret, platform).Reveal())
                : null);
    }

    /// <summary>
    /// What the server needs to list the realm users of a tenant row, or <c>null</c> when the row has none: a preview
    /// tenant, a row without a users client secret, or a row whose <c>Oidc</c> authority does not end in <c>realms/</c>
    /// and the tenant id. The realm name is the tenant id. The secret stays encrypted until it is revealed.
    /// </summary>
    public static TenantRealmUsers? RealmUsers(TenantRow row, TenantSecretCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(cipher);

        var id = row.Id;
        var stored = row.RealmUsersClientSecret;
        if (row.PreviewOf is not null || stored is not { Length: > 0 } || TenantRealmAddress.BaseOf(row.OidcAuthority, id) is not { } baseAddress)
        {
            return null;
        }

        return new TenantRealmUsers(
            baseAddress,
            id,
            SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.RealmUsersClientSecret, stored).Reveal()));
    }

    /// <summary>The deployment bindings of a tenant keyed by name, ignoring letter case. A secret stays encrypted until it is revealed.</summary>
    public static IReadOnlyDictionary<string, TenantBinding> Bindings(string tenantId, IEnumerable<TenantBindingRow> rows, TenantSecretCipher cipher)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(cipher);

        var bindings = new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in rows)
        {
            bindings[binding.Name] = binding.Kind == DeploymentBindingKinds.Secret
                ? new TenantBinding(
                    DeploymentBindingKinds.Secret,
                    null,
                    SecretValue.Deferred(() => cipher.Decrypt(tenantId, TenantSecretFields.Binding(binding.Name), binding.SecretValue ?? []).Reveal()))
                : new TenantBinding(binding.Kind, binding.Url, null);
        }

        return bindings;
    }

    // A tenant has its own mail settings exactly when its SMTP host is set. A preview tenant's are its own, bound to its own id.
    private static EmailSettings? Email(TenantRow row, TenantSecretCipher cipher)
    {
        if (Blank(row.SmtpHost) is not { } host)
        {
            return null;
        }

        var id = row.Id;
        var stored = row.SmtpPassword;
        return new EmailSettings(
            host,
            row.SmtpPort ?? EmailSettings.DefaultPort,
            row.SmtpUseTls ?? true,
            Blank(row.SmtpUserName),
            stored is { Length: > 0 } ? SecretValue.Deferred(() => cipher.Decrypt(id, TenantSecretFields.SmtpPassword, stored).Reveal()) : null,
            Blank(row.EmailFrom));
    }

    // The current key comes first, then the previous one. Each is decrypted only at the moment of use.
    private static List<TenantClientKey> ClientKeys(TenantRow signIn, TenantSecretCipher cipher)
    {
        var keys = new List<TenantClientKey>();
        AddKey(keys, signIn, signIn.OidcClientKeyId, signIn.OidcClientKey, cipher);
        AddKey(keys, signIn, signIn.OidcPreviousClientKeyId, signIn.OidcPreviousClientKey, cipher);
        return keys;
    }

    private static void AddKey(List<TenantClientKey> keys, TenantRow signIn, string? keyId, byte[]? encrypted, TenantSecretCipher cipher)
    {
        if (encrypted is { Length: > 0 })
        {
            keys.Add(new TenantClientKey(
                keyId ?? "",
                SecretValue.Deferred(() => cipher.Decrypt(signIn.Id, TenantSecretFields.OidcClientKey, encrypted).Reveal())));
        }
    }

    // A blank value counts as missing.
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
