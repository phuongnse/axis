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

            AddBindingProblems(tenantId, settings, problems);

            foreach (var host in hosts.Where(host => !string.IsNullOrWhiteSpace(host)))
            {
                if (!tenantsByHost.TryAdd(host, tenantId) && tenantsByHost[host] != tenantId)
                {
                    problems.Add($"Host '{host}' is configured for both tenant '{tenantsByHost[host]}' and tenant '{tenantId}'.");
                }
            }
        }

        AddPreviewProblems(options, problems);
        return problems;
    }

    // A tenant whose PreviewOf is its own id is a preview tenant, so the second rule refuses it.
    private static void AddPreviewProblems(TenantOptions options, List<string> problems)
    {
        var previewsByMain = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (tenantId, settings) in options.Tenants.OrderBy(tenant => tenant.Key, StringComparer.Ordinal))
        {
            var main = settings?.PreviewOf;
            if (string.IsNullOrWhiteSpace(main))
            {
                continue;
            }

            if (!options.Tenants.TryGetValue(main, out var mainSettings))
            {
                problems.Add($"Tenant '{tenantId}' names '{main}' in PreviewOf, which is not a configured tenant.");
            }
            else if (!string.IsNullOrWhiteSpace(mainSettings?.PreviewOf))
            {
                problems.Add($"Tenant '{tenantId}' names '{main}' in PreviewOf, which is itself a preview tenant.");
            }
            else if (!previewsByMain.TryAdd(main, tenantId))
            {
                problems.Add($"Tenants '{previewsByMain[main]}' and '{tenantId}' both name '{main}' in PreviewOf. A tenant has at most one preview tenant.");
            }
        }
    }

    private static void AddBindingProblems(string tenantId, TenantSettings? settings, List<string> problems)
    {
        foreach (var (name, binding) in (settings?.Bindings ?? []).OrderBy(binding => binding.Key, StringComparer.Ordinal))
        {
            AddBindingProblems(tenantId, name, binding?.Kind, !string.IsNullOrWhiteSpace(binding?.Value), binding?.Value, problems);
        }
    }

    /// <summary>
    /// Adds the problems of one binding. <paramref name="hasValue"/> tells whether the binding holds a value, and
    /// <paramref name="value"/> is the value a <c>url</c> or <c>storage</c> binding is checked on. A binding's value never goes into a
    /// message, so a bad configuration cannot leak a secret.
    /// </summary>
    internal static void AddBindingProblems(string tenantId, string name, string? kind, bool hasValue, string? value, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            problems.Add($"Tenant '{tenantId}' binding '{name}' has no kind: use '{DeploymentBindingKinds.Url}', '{DeploymentBindingKinds.Secret}' or '{DeploymentBindingKinds.Storage}'.");
        }
        else if (kind is not (DeploymentBindingKinds.Url or DeploymentBindingKinds.Secret or DeploymentBindingKinds.Storage))
        {
            problems.Add($"Tenant '{tenantId}' binding '{name}' has kind '{kind}': use '{DeploymentBindingKinds.Url}', '{DeploymentBindingKinds.Secret}' or '{DeploymentBindingKinds.Storage}'.");
        }

        if (!hasValue)
        {
            problems.Add($"Tenant '{tenantId}' binding '{name}' has no value.");
        }
        else if (kind == DeploymentBindingKinds.Url && !IsAbsoluteHttpUrl(value))
        {
            problems.Add($"Tenant '{tenantId}' binding '{name}' is not an absolute http or https URL.");
        }
        else if (kind == DeploymentBindingKinds.Storage && value != StorageBindingValues.Database)
        {
            problems.Add($"Tenant '{tenantId}' binding '{name}' is a storage binding whose value is not '{StorageBindingValues.Database}'.");
        }
    }

    private static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsValidTenantId(string tenantId) =>
        tenantId.Length > 0 && tenantId.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
