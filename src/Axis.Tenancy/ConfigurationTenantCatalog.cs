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
    /// connection and no sign-in settings, which <see cref="Load"/> reads from the configuration section.
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

            return new TenantEntry(
                id,
                [.. settings.Hosts],
                new SecretValue(settings.ConnectionString!),
                Secret(tenantSection?["SchemaConnectionString"]),
                Oidc(tenantSection?.GetSection("Oidc")),
                bindings);
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
                section["Claims:Roles"]);

    // A blank value counts as missing.
    private static SecretValue? Secret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : new SecretValue(value);
}
