using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>
/// Checks that no <c>secret</c> deployment binding takes its value from a configuration file. Outside Development a
/// secret comes from the environment: environment variables, the command line or mounted secret files.
/// </summary>
public static class SecretBindingSources
{
    /// <summary>
    /// Returns one message per secret binding whose value comes from a file; an empty list means every secret comes
    /// from the environment. The source that wins counts, so a file may hold a placeholder that the environment
    /// overrides. No message names a value.
    /// </summary>
    public static IReadOnlyList<string> Validate(IConfiguration configuration, TenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();
        foreach (var (tenantId, settings) in options.Tenants.OrderBy(tenant => tenant.Key, StringComparer.Ordinal))
        {
            var secrets = (settings?.Bindings ?? [])
                .Where(binding => binding.Value?.Kind == DeploymentBindingKinds.Secret)
                .Select(binding => binding.Key)
                .Order(StringComparer.Ordinal);
            foreach (var name in secrets)
            {
                if (configuration is not IConfigurationRoot root)
                {
                    problems.Add($"Tenant '{tenantId}' binding '{name}' is a secret whose configuration source cannot be checked.");
                    continue;
                }

                var key = ConfigurationPath.Combine(TenantOptions.SectionName, tenantId, nameof(TenantSettings.Bindings), name, nameof(DeploymentBindingSettings.Value));
                if (WinningProvider(root, key) is FileConfigurationProvider file)
                {
                    problems.Add(
                        $"Tenant '{tenantId}' binding '{name}' is a secret set in the file '{file.Source.Path}'. "
                        + $"Outside Development, set it with an environment variable such as 'Tenants__{tenantId}__Bindings__{name}__Value'.");
                }
            }
        }

        return problems;
    }

    // The last provider that has the key wins. A chained configuration is searched in turn, so a file behind it counts.
    private static IConfigurationProvider? WinningProvider(IConfigurationRoot root, string key)
    {
        var provider = root.Providers.Reverse().FirstOrDefault(provider => provider.TryGet(key, out _));
        return provider is ChainedConfigurationProvider { Configuration: IConfigurationRoot chained }
            ? WinningProvider(chained, key)
            : provider;
    }
}
