using Axis.Configuration.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Axis.Tenancy;

/// <summary>The tenant catalog built from the <c>Tenants</c> configuration section, the <c>configuration</c> source.</summary>
public sealed class ConfigurationTenantCatalog : ITenantCatalog
{
    private readonly Dictionary<string, TenantEntry> _tenantsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TenantEntry> _tenantsByHost = new(StringComparer.OrdinalIgnoreCase);

    public ConfigurationTenantCatalog(IEnumerable<TenantEntry> tenants)
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

    public bool IsUnavailableHost(string host) => false;

    /// <summary>
    /// Reads and checks the <c>Tenants</c> section. Outside Development a secret binding may not take its value from
    /// a configuration file.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The tenant configuration is invalid, or outside Development a secret binding takes its value from a
    /// configuration file.
    /// </exception>
    public static ConfigurationTenantCatalog Load(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new TenantOptions();
        var section = configuration.GetSection(TenantOptions.SectionName);
        section.Bind(options.Tenants);

        var problems = TenantOptionsValidator.Validate(options).ToList();
        foreach (var (id, settings) in options.Tenants.OrderBy(tenant => tenant.Key, StringComparer.Ordinal))
        {
            EmailSettings.Read(section.GetSection(id).GetSection("Email"), $"{TenantOptions.SectionName}:{id}:Email", problems);
            if (!string.IsNullOrWhiteSpace(settings.PreviewOf) && section.GetSection(id).GetSection("Oidc").Exists())
            {
                problems.Add(
                    $"Tenant '{id}' is a preview tenant and has its own Oidc block. A preview tenant uses the Oidc settings of its main tenant '{settings.PreviewOf}'.");
            }

            AddRealmProblems(section.GetSection(id), id, settings.PreviewOf, problems);
            AddRealmUsersProblems(section.GetSection(id), id, settings.PreviewOf, problems);
            AddLegalProblems(section.GetSection(id), id, settings.PreviewOf, problems);
        }

        if (!environment.IsDevelopment())
        {
            problems.AddRange(SecretBindingSources.Validate(configuration, options));
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The tenant configuration is invalid. {string.Join(" ", problems)}");
        }

        return new ConfigurationTenantCatalog(Entries(options, section));
    }

    // A blank ActivationClientSecret means the tenant has no realm details. With a secret, the realm needs its Name and an
    // Oidc authority that ends in /realms/<Name>. A message names a setting and never holds a value.
    private static void AddRealmProblems(IConfigurationSection tenant, string id, string? previewOf, List<string> problems)
    {
        var realm = tenant.GetSection("Realm");
        var prefix = $"{TenantOptions.SectionName}:{id}";
        if (!string.IsNullOrWhiteSpace(previewOf))
        {
            if (realm.Exists())
            {
                problems.Add($"Tenant '{id}' is a preview tenant and has its own Realm block. A preview tenant has no realm details of its own.");
            }

            return;
        }

        if (Blank(realm["ActivationClientSecret"]) is null)
        {
            return;
        }

        if (Blank(realm["Name"]) is not { } name)
        {
            problems.Add($"Tenant '{id}' sets {prefix}:Realm:ActivationClientSecret but has no {prefix}:Realm:Name.");
        }
        else if (TenantRealmAddress.BaseOf(tenant["Oidc:Authority"], name) is null)
        {
            problems.Add($"Tenant '{id}' sets {prefix}:Realm:ActivationClientSecret, so {prefix}:Oidc:Authority must be an http or https address that ends in /realms/{Uri.EscapeDataString(name)}.");
        }
    }

    // A blank UsersClientSecret means the tenant has no users client. With a secret, the Oidc authority must end in
    // /realms/<realm>. A preview tenant's own Oidc block is rejected already. A message names a setting and never holds a value.
    private static void AddRealmUsersProblems(IConfigurationSection tenant, string id, string? previewOf, List<string> problems)
    {
        if (!string.IsNullOrWhiteSpace(previewOf) || Blank(tenant["Oidc:UsersClientSecret"]) is null || TenantRealmAddress.Split(tenant["Oidc:Authority"]) is not null)
        {
            return;
        }

        var prefix = $"{TenantOptions.SectionName}:{id}";
        problems.Add($"Tenant '{id}' sets {prefix}:Oidc:UsersClientSecret, so {prefix}:Oidc:Authority must be an http or https address that ends in /realms/<realm>.");
    }

    // The same checks as `tenant legal set`, so a tenant can move between the sources. A preview tenant takes its main
    // tenant's block. A message names a setting and never holds a value.
    private static void AddLegalProblems(IConfigurationSection tenant, string id, string? previewOf, List<string> problems)
    {
        var legal = tenant.GetSection("Legal");
        var prefix = $"{TenantOptions.SectionName}:{id}:Legal";
        if (!string.IsNullOrWhiteSpace(previewOf))
        {
            if (legal.Exists())
            {
                problems.Add($"Tenant '{id}' is a preview tenant and has its own Legal block. A preview tenant uses the Legal settings of its main tenant '{previewOf}'.");
            }

            return;
        }

        AddAddressProblem(legal["PrivacyUrl"], $"{prefix}:PrivacyUrl", problems);
        AddAddressProblem(legal["TermsUrl"], $"{prefix}:TermsUrl", problems);
        var (terms, version) = (Blank(legal["TermsUrl"]), Blank(legal["TermsVersion"]));
        if (terms is not null && version is null)
        {
            problems.Add($"'{prefix}:TermsVersion' is required when '{prefix}:TermsUrl' is set.");
        }
        else if (terms is null && version is not null)
        {
            problems.Add($"'{prefix}:TermsUrl' is required when '{prefix}:TermsVersion' is set.");
        }

        foreach (var site in legal.GetSection("Sites").GetChildren())
        {
            var sitePrefix = $"{prefix}:Sites:{site.Key}";
            if (!SitePaths.IsValid(site.Key) || SitePaths.Reserved.Contains(site.Key.ToLowerInvariant()))
            {
                problems.Add($"'{sitePrefix}' is not a valid site path, or the platform keeps it.");
            }

            AddAddressProblem(site["PrivacyUrl"], $"{sitePrefix}:PrivacyUrl", problems);
            AddAddressProblem(site["TermsUrl"], $"{sitePrefix}:TermsUrl", problems);
            if (Blank(site["TermsVersion"]) is not null)
            {
                problems.Add($"'{sitePrefix}:TermsVersion' is not allowed. A site takes the terms version of its tenant.");
            }
        }
    }

    private static void AddAddressProblem(string? value, string setting, List<string> problems)
    {
        if (Blank(value) is { } address && !(Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            problems.Add($"'{setting}' must be an absolute https address.");
        }
    }

    // The tenant's links, or null when it sets none. A site that sets neither address is left out.
    private static TenantLegal? Legal(IConfigurationSection tenant)
    {
        var legal = tenant.GetSection("Legal");
        var sites = new Dictionary<string, SiteLegal>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in legal.GetSection("Sites").GetChildren())
        {
            var own = new SiteLegal(Blank(site["PrivacyUrl"])?.Trim(), Blank(site["TermsUrl"])?.Trim());
            if (own.PrivacyUrl is not null || own.TermsUrl is not null)
            {
                sites[site.Key.ToLowerInvariant()] = own;
            }
        }

        var (privacyUrl, termsUrl, termsVersion) = (Blank(legal["PrivacyUrl"])?.Trim(), Blank(legal["TermsUrl"])?.Trim(), Blank(legal["TermsVersion"])?.Trim());
        return privacyUrl is null && termsUrl is null && termsVersion is null && sites.Count == 0
            ? null
            : new TenantLegal(privacyUrl, termsUrl, termsVersion, sites);
    }

    private static TenantRealmUsers? RealmUsers(IConfigurationSection tenant, bool preview)
    {
        if (preview || Secret(tenant["Oidc:UsersClientSecret"]) is not { } secret || TenantRealmAddress.Split(tenant["Oidc:Authority"]) is not { } address)
        {
            return null;
        }

        return new TenantRealmUsers(address.BaseAddress, address.Realm, secret);
    }

    private static TenantRealmAccess? Realm(IConfigurationSection tenant, bool preview)
    {
        var realm = tenant.GetSection("Realm");
        if (preview || Secret(realm["ActivationClientSecret"]) is not { } secret || Blank(realm["Name"]) is not { } name)
        {
            return null;
        }

        return TenantRealmAddress.BaseOf(tenant["Oidc:Authority"], name) is { } baseAddress
            ? new TenantRealmAccess(baseAddress, name, secret, Secret(tenant["Oidc:ClientSecret"]))
            : null;
    }

    /// <summary>
    /// Builds a catalog from tenant options that already passed <see cref="TenantOptionsValidator"/>. It has no schema
    /// connection, no sign-in settings and no realm details, which <see cref="Load"/> reads from the configuration section. It does not
    /// copy a main tenant's sign-in settings into its preview tenant.
    /// </summary>
    public static ConfigurationTenantCatalog FromOptions(TenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ConfigurationTenantCatalog(Entries(options, section: null));
    }

    private static IEnumerable<TenantEntry> Entries(TenantOptions options, IConfigurationSection? section) =>
        options.Tenants.Select(tenant =>
        {
            var (id, settings) = (tenant.Key, tenant.Value);
            var tenantSection = section?.GetSection(id);
            var bindings = new Dictionary<string, TenantBinding>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, binding) in settings.Bindings)
            {
                bindings[name] = binding.Kind == DeploymentBindingKinds.Secret
                    ? new TenantBinding(DeploymentBindingKinds.Secret, null, new SecretValue(binding.Value!))
                    : new TenantBinding(binding.Kind!, binding.Value, null);
            }

            // A preview tenant signs people in with its main tenant's settings, but never takes the main tenant's
            // EndpointAudience or ServiceAudience, so it accepts no machine-caller or service account bearer token.
            // Its bindings are only its own.
            var main = string.IsNullOrWhiteSpace(settings.PreviewOf) ? null : settings.PreviewOf;
            var oidc = main is null
                ? Oidc(tenantSection?.GetSection("Oidc"))
                : Oidc(section?.GetSection(main).GetSection("Oidc")) is { } mainOidc ? mainOidc with { EndpointAudience = null, ServiceAudience = null } : null;

            return new TenantEntry(
                id,
                [.. settings.Hosts],
                new SecretValue(settings.ConnectionString!),
                Secret(tenantSection?["SchemaConnectionString"]),
                oidc,
                bindings,
                main,
                tenantSection is null ? null : EmailSettings.Read(tenantSection.GetSection("Email"), $"{TenantOptions.SectionName}:{id}:Email", []),
                tenantSection is null ? null : Realm(tenantSection, main is not null),
                tenantSection is null ? null : RealmUsers(tenantSection, main is not null),
                HostSites: settings.SiteHosts.Count == 0
                    ? null
                    : new Dictionary<string, string>(settings.SiteHosts.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.ToLowerInvariant())), StringComparer.OrdinalIgnoreCase),
                Legal: tenantSection is null ? null : Legal(main is null ? tenantSection : section!.GetSection(main)));
        });

    private static TenantOidc? Oidc(IConfigurationSection? section) =>
        section is null || !section.Exists()
            ? null
            : new TenantOidc(
                section["Authority"],
                section["ClientId"],
                Secret(section["ClientSecret"]),
                section["EndpointAudience"],
                section["ServiceAudience"],
                section["Claims:UserId"],
                section["Claims:DisplayName"],
                section["Claims:Roles"],
                section["Claims:Email"],
                section["Claims:Locale"],
                Blank(section["Profile"]),
                ClientKeys(section));

    private static IReadOnlyList<TenantClientKey> ClientKeys(IConfigurationSection section) =>
        Secret(section["ClientKey"]) is { } key ? [new TenantClientKey(section["ClientKeyId"] ?? "", key)] : [];

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // A blank value counts as missing.
    private static SecretValue? Secret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new SecretValue(value);
}
