using System.Text.RegularExpressions;
using Npgsql;

namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane</c> settings of the service, see the settings table in
/// <c>docs/reference/control-plane.md#operators</c>. Every setting is required, and a bad value stops the start.
/// A message names the setting and never holds its value.
/// </summary>
public sealed partial class ControlPlaneOptions
{
    public const string SectionName = "ControlPlane";

    /// <summary>The region code, such as <c>vn</c>.</summary>
    public string? Region { get; set; }

    /// <summary>The domain of the tenant subdomains of the region, such as <c>vn.example.com</c>.</summary>
    public string? TenantDomain { get; set; }

    /// <summary>The connection string of the control plane database.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Reads and checks the settings, and returns them.</summary>
    /// <exception cref="InvalidOperationException">
    /// A setting is missing or wrong. The message names every such setting and never a value.
    /// </exception>
    public static ControlPlaneOptions Load(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var options = new ControlPlaneOptions();
        configuration.GetSection(SectionName).Bind(options);

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Region))
        {
            problems.Add(Required(nameof(Region)));
        }
        else if (!RegionCode().IsMatch(options.Region))
        {
            problems.Add($"The setting {SectionName}:{nameof(Region)} must be lowercase letters and digits only.");
        }

        if (string.IsNullOrWhiteSpace(options.TenantDomain))
        {
            problems.Add(Required(nameof(TenantDomain)));
        }
        else if (Uri.CheckHostName(options.TenantDomain) != UriHostNameType.Dns)
        {
            problems.Add($"The setting {SectionName}:{nameof(TenantDomain)} must be a domain name.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            problems.Add(Required(nameof(ConnectionString)));
        }
        else if (ConnectionStringProblem(options.ConnectionString, RequiresVerifiedTls(environment)) is { } problem)
        {
            problems.Add(problem);
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The control plane configuration is invalid. {string.Join(" ", problems)}");
        }

        return options;
    }

    private static string Required(string name) => $"The setting {SectionName}:{name} is required.";

    // The rule of the platform's hosts for their own databases: every environment except Development and Testing.
    private static bool RequiresVerifiedTls(IHostEnvironment environment) =>
        !environment.IsDevelopment() && !environment.IsEnvironment("Testing");

    private static string? ConnectionStringProblem(string connectionString, bool requireVerifyFull)
    {
        var setting = $"{SectionName}:{nameof(ConnectionString)}";
        try
        {
            if (requireVerifyFull && new NpgsqlConnectionStringBuilder(connectionString).SslMode != SslMode.VerifyFull)
            {
                return $"The setting {setting} must set SSL Mode=VerifyFull outside Development.";
            }

            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return $"The setting {setting} is not a valid connection string.";
        }

        return null;
    }

    [GeneratedRegex("^[a-z0-9]+$")]
    private static partial Regex RegionCode();
}
