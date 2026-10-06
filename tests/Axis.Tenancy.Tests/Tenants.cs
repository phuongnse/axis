namespace Axis.Tenancy.Tests;

/// <summary>Builds tenant options for tests.</summary>
internal static class Tenants
{
    public const string ConnectionString = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis";

    public static TenantOptions Options(params (string Id, string[] Hosts, string? ConnectionString)[] tenants)
    {
        var options = new TenantOptions();
        foreach (var (id, hosts, connectionString) in tenants)
        {
            var settings = new TenantSettings { ConnectionString = connectionString };
            settings.Hosts.AddRange(hosts);
            options.Tenants.Add(id, settings);
        }

        return options;
    }
}
