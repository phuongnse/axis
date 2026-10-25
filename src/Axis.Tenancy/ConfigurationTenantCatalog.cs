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
            if (!string.IsNullOrWhiteSpace(settings.PreviewOf) && section.GetSection(id).GetSection("Oidc").Exists())
            {
                problems.Add(
                    $"Tenant '{id}' is a preview tenant and has its own Oidc block. A preview tenant uses the Oidc settings of its main tenant '{settings.PreviewOf}'.");
            }
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

    /// <summary>
    /// Builds a catalog from tenant options that already passed <see cref="TenantOptionsValidator"/>. It has no schema
    /// connection and no sign-in settings, which <see cref="Load"/> reads from the configuration section. It does not
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
            // EndpointAudience, so it accepts no machine-caller bearer token. Its bindings are only its own.
            var main = string.IsNullOrWhiteSpace(settings.PreviewOf) ? null : settings.PreviewOf;
            var oidc = main is null
                ? Oidc(tenantSection?.GetSection("Oidc"))
                : Oidc(section?.GetSection(main).GetSection("Oidc")) is { } mainOidc ? mainOidc with { EndpointAudience = null } : null;

            return new TenantEntry(
                id,
                [.. settings.Hosts],
                new SecretValue(settings.ConnectionString!),
                Secret(tenantSection?["SchemaConnectionString"]),
                oidc,
                bindings,
                main);
        });

    private static TenantOidc? Oidc(IConfigurationSection? section) =>
        section is null || !section.Exists()
            ? null
            : new TenantOidc(
                section["Authority"],
                section["ClientId"],
                Secret(section["ClientSecret"]),
                section["EndpointAudience"],
                section["Claims:UserId"],
                section["Claims:DisplayName"],
                section["Claims:Roles"],
                section["Claims:Email"],
                section["Claims:Locale"]);

    // A blank value counts as missing.
    private static SecretValue? Secret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new SecretValue(value);
}
