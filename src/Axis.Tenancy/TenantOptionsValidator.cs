namespace Axis.Tenancy;

/// <summary>Checks the tenant configuration before the server starts.</summary>
public static class TenantOptionsValidator
{
    /// <summary>Returns one message per problem; an empty list means the configuration is valid.</summary>
    public static IReadOnlyList<string> Validate(TenantOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Tenants.Count == 0)
        {
            return [$"No tenants are configured. Add at least one tenant under '{TenantOptions.SectionName}'."];
        }

        var problems = new List<string>();
        var tenantsByHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (tenantId, settings) in options.Tenants.OrderBy(tenant => tenant.Key, StringComparer.Ordinal))
        {
            if (!IsValidTenantId(tenantId))
            {
                problems.Add($"Tenant id '{tenantId}' is invalid: use only lowercase letters, digits and hyphens.");
            }

            var hosts = settings?.Hosts ?? [];
            if (hosts.Count == 0)
            {
                problems.Add($"Tenant '{tenantId}' has no hosts.");
            }

            if (hosts.Any(string.IsNullOrWhiteSpace))
            {
                problems.Add($"Tenant '{tenantId}' has a blank host.");
            }

            if (string.IsNullOrWhiteSpace(settings?.ConnectionString))
            {
                problems.Add($"Tenant '{tenantId}' has no connection string.");
            }

            foreach (var host in hosts.Where(host => !string.IsNullOrWhiteSpace(host)))
            {
                if (!tenantsByHost.TryAdd(host, tenantId) && tenantsByHost[host] != tenantId)
                {
                    problems.Add($"Host '{host}' is configured for both tenant '{tenantsByHost[host]}' and tenant '{tenantId}'.");
                }
            }
        }

        return problems;
    }

    private static bool IsValidTenantId(string tenantId) =>
        tenantId.Length > 0 && tenantId.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
