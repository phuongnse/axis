using System.Globalization;
using System.Text.RegularExpressions;
using Axis.ControlPlane.Cells;
using Npgsql;

namespace Axis.ControlPlane;

/// <summary>
/// The <c>ControlPlane</c> settings of the service, see the settings table in
/// <c>docs/reference/control-plane.md#operators</c>. The region, the tenant domain and the connection string are
/// required. The cell settings are optional, and once a cell is listed, all of them are required. A bad value stops
/// the start. A message names the setting and never holds its value.
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

    /// <summary>The cells of the region. Each is reached over mutual TLS, see <see cref="CellOptions"/>.</summary>
    public List<CellOptions> Cells { get; } = [];

    /// <summary>
    /// The client secret of each cell's operator realm client, by cell id. It comes from the key-per-file secret
    /// <c>ControlPlane__CellSecrets__&lt;cell id&gt;</c>, never from a plain setting.
    /// </summary>
    public Dictionary<string, string> CellSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The PEM certificate and private key that the control plane presents to every cell.</summary>
    public CellClientCertificateOptions? CellClientCertificate { get; set; }

    /// <summary>
    /// The PEM file of the region's internal certificate authority, which the cells' server certificates must chain to.
    /// Without it the system trust store decides.
    /// </summary>
    public string? CellCertificateAuthorityPath { get; set; }

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

        if (options.Cells.Count > 0)
        {
            problems.AddRange(CellProblems(options));
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The control plane configuration is invalid. {string.Join(" ", problems)}");
        }

        return options;
    }

    private static List<string> CellProblems(ControlPlaneOptions options)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Cells.Count; index++)
        {
            var cell = options.Cells[index];
            var prefix = $"{SectionName}:{nameof(Cells)}:{index.ToString(CultureInfo.InvariantCulture)}";
            var idIsValid = false;
            if (string.IsNullOrWhiteSpace(cell?.Id))
            {
                problems.Add($"The setting {prefix}:{nameof(CellOptions.Id)} is required.");
            }
            else if (!CellId().IsMatch(cell.Id))
            {
                problems.Add($"The setting {prefix}:{nameof(CellOptions.Id)} must be lowercase letters and digits, joined by single hyphens.");
            }
            else if (!seen.Add(cell.Id))
            {
                problems.Add($"The setting {prefix}:{nameof(CellOptions.Id)} repeats the id of an earlier cell.");
            }
            else
            {
                idIsValid = true;
            }

            AddHttpsUrlProblem(problems, $"{prefix}:{nameof(CellOptions.InternalUrl)}", cell?.InternalUrl);
            AddHttpsUrlProblem(problems, $"{prefix}:{nameof(CellOptions.TokenUrl)}", cell?.TokenUrl);
            if (string.IsNullOrWhiteSpace(cell?.ClientId))
            {
                problems.Add($"The setting {prefix}:{nameof(CellOptions.ClientId)} is required.");
            }

            if (idIsValid && !(options.CellSecrets.TryGetValue(cell!.Id!, out var secret) && !string.IsNullOrWhiteSpace(secret)))
            {
                problems.Add($"The setting {SectionName}:{nameof(CellSecrets)}:{cell.Id} is required.");
            }
        }

        var certificate = $"{SectionName}:{nameof(CellClientCertificate)}";
        AddFileProblem(problems, $"{certificate}:{nameof(CellClientCertificateOptions.Path)}", options.CellClientCertificate?.Path);
        AddFileProblem(problems, $"{certificate}:{nameof(CellClientCertificateOptions.KeyPath)}", options.CellClientCertificate?.KeyPath);
        if (!string.IsNullOrWhiteSpace(options.CellCertificateAuthorityPath) && !File.Exists(options.CellCertificateAuthorityPath))
        {
            problems.Add($"The file of the setting {SectionName}:{nameof(CellCertificateAuthorityPath)} does not exist.");
        }

        return problems;
    }

    private static void AddHttpsUrlProblem(List<string> problems, string setting, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"The setting {setting} is required.");
        }
        else if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add($"The setting {setting} must be an absolute https URL.");
        }
    }

    private static void AddFileProblem(List<string> problems, string setting, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            problems.Add($"The setting {setting} is required when a cell is listed.");
        }
        else if (!File.Exists(path))
        {
            problems.Add($"The file of the setting {setting} does not exist.");
        }
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

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CellId();
}
