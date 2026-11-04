using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy;

/// <summary>
/// The <c>Cell</c> settings of a host, see the cell settings table in <c>docs/reference/host-settings.md#cell-settings</c>.
/// A host is part of an Axis SaaS cell when <see cref="Id"/> is set. Every cell feature asks <see cref="IsCell"/>.
/// </summary>
public sealed class CellOptions
{
    public const string SectionName = "Cell";

    /// <summary>The operator realm when <c>Cell:OperatorRealm</c> is not set.</summary>
    public const string DefaultOperatorRealm = "axis-operators";

    /// <summary>The console client when <c>Cell:ConsoleClientId</c> is not set.</summary>
    public const string DefaultConsoleClientId = "axis-cell-console";

    /// <summary>The operator role when <c>Cell:OperatorRole</c> is not set.</summary>
    public const string DefaultOperatorRole = "cell-operator";

    /// <summary>The cell id, such as <c>vn-1</c>. Setting it turns on quotas, per-tenant Studio folders and the cell console.</summary>
    public string? Id { get; set; }

    /// <summary>The region code, such as <c>vn</c>.</summary>
    public string? Region { get; set; }

    /// <summary>The domain of the tenant subdomains, such as <c>vn.example.com</c>.</summary>
    public string? TenantDomain { get; set; }

    /// <summary>A domain no ingress serves, for scratch tenants of the restore check.</summary>
    public string? InternalDomain { get; set; }

    /// <summary>The measured number of tenants the cell holds.</summary>
    public int? TenantCap { get; set; }

    /// <summary>The host of the cell console.</summary>
    public string? ConsoleHost { get; set; }

    /// <summary>Address ranges that may reach the console host. Empty allows every address.</summary>
    public List<string> ConsoleAllowedNetworks { get; } = [];

    /// <summary>The internal listener of the provisioning API.</summary>
    public string? InternalUrl { get; set; }

    /// <summary>The operator realm, <c>axis-operators</c> by default. A blank value keeps the default.</summary>
    public string OperatorRealm { get; set; } = DefaultOperatorRealm;

    /// <summary>
    /// The role of the operator realm that the cell console and the API need, <c>cell-operator</c> by default.
    /// A blank value keeps the default.
    /// </summary>
    public string OperatorRole { get; set; } = DefaultOperatorRole;

    /// <summary>
    /// The client of the operator realm that the cell console signs in with, <c>axis-cell-console</c> by default. A blank
    /// value keeps the default. Its secret is <c>Cell:ConsoleClientSecret</c>, which only the server reads.
    /// </summary>
    public string ConsoleClientId { get; set; } = DefaultConsoleClientId;

    /// <summary>The file of the cell's backup key, which wraps each tenant's backup key.</summary>
    public string? BackupKeyFile { get; set; }

    /// <summary>True when <c>Cell:Id</c> is set. Without it the host is a self-hosted installation.</summary>
    public bool IsCell => !string.IsNullOrWhiteSpace(Id);

    /// <summary>
    /// Reads the <c>Cell</c> section. Without <c>Cell:Id</c> no other setting is checked and <c>Tenancy:Source</c> is not read.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A value cannot be read, or a host in a cell lacks <c>Tenancy:Source=directory</c>, <c>Cell:Region</c> or
    /// <c>Cell:TenantDomain</c>, or <c>Cell:Id</c> is not the region, a hyphen and a number.
    /// </exception>
    public static CellOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new CellOptions();
        configuration.GetSection(SectionName).Bind(options);
        if (string.IsNullOrWhiteSpace(options.OperatorRealm))
        {
            options.OperatorRealm = DefaultOperatorRealm;
        }

        if (string.IsNullOrWhiteSpace(options.OperatorRole))
        {
            options.OperatorRole = DefaultOperatorRole;
        }

        if (string.IsNullOrWhiteSpace(options.ConsoleClientId))
        {
            options.ConsoleClientId = DefaultConsoleClientId;
        }

        if (!options.IsCell)
        {
            return options;
        }

        var problems = CellOptionsValidator.Validate(options, TenancySettings.Source(configuration));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The cell configuration is invalid. {string.Join(" ", problems)}");
        }

        return options;
    }
}
