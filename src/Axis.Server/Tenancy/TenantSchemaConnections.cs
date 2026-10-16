using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// The connection string of each tenant's schema database role, from the <c>SchemaConnectionString</c>
/// setting of the tenant in the <c>Tenants</c> section. Only startup activation uses it. Only the
/// server reads it: the worker shares <see cref="TenantSettings"/> and never binds it.
/// </summary>
internal sealed class TenantSchemaConnections
{
    public const string SettingName = "SchemaConnectionString";

    private readonly IReadOnlyDictionary<string, string> _connectionStrings;

    private TenantSchemaConnections(IReadOnlyDictionary<string, string> connectionStrings) =>
        _connectionStrings = connectionStrings;

    /// <summary>The schema role's connection string for the tenant.</summary>
    public string For(string tenantId) => _connectionStrings[tenantId];

    /// <summary>Reads the setting of every tenant and checks it.</summary>
    /// <exception cref="InvalidOperationException">A tenant has no <c>SchemaConnectionString</c>, or it is blank.</exception>
    public static TenantSchemaConnections Load(IConfiguration configuration)
    {
        var connectionStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var tenants = configuration.GetSection(TenantOptions.SectionName).GetChildren()
            .OrderBy(tenant => tenant.Key, StringComparer.Ordinal);
        foreach (var tenant in tenants)
        {
            var value = tenant[SettingName];
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"Tenant '{tenant.Key}' has no {SettingName}.");
                continue;
            }

            connectionStrings[tenant.Key] = value;
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The schema connection configuration is invalid. {string.Join(" ", problems)}");
        }

        return new TenantSchemaConnections(connectionStrings);
    }
}
