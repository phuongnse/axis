using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// The connection string of each tenant's schema database role, from the tenant catalog. Only startup
/// activation uses it. Only the server needs it: the worker's tenants have none and it never uses one.
/// </summary>
internal sealed class TenantSchemaConnections
{
    public const string SettingName = "SchemaConnectionString";

    private readonly IReadOnlyDictionary<string, SecretValue> _connectionStrings;

    private TenantSchemaConnections(IReadOnlyDictionary<string, SecretValue> connectionStrings) =>
        _connectionStrings = connectionStrings;

    /// <summary>The schema role's connection string for the tenant.</summary>
    public string For(string tenantId) => _connectionStrings[tenantId].Reveal();

    /// <summary>Reads the setting of every tenant and checks it.</summary>
    /// <exception cref="InvalidOperationException">A tenant has no <c>SchemaConnectionString</c>, or it is blank.</exception>
    public static TenantSchemaConnections Load(ITenantCatalog catalog)
    {
        var connectionStrings = new Dictionary<string, SecretValue>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var tenant in catalog.Tenants)
        {
            if (tenant.SchemaConnectionString is null)
            {
                problems.Add($"Tenant '{tenant.Id}' has no {SettingName}.");
                continue;
            }

            connectionStrings[tenant.Id] = tenant.SchemaConnectionString;
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The schema connection configuration is invalid. {string.Join(" ", problems)}");
        }

        return new TenantSchemaConnections(connectionStrings);
    }
}
