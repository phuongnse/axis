using System.Globalization;

namespace Axis.Mcp.Configuration;

public sealed record AxisMcpOptions
{
    public const string ApiBaseUrlEnvironmentVariable = "AXIS_MCP_API_BASE_URL";
    public const string AccessEnvironmentVariable = "AXIS_MCP_ACCESS";
    public const string RootCaPathEnvironmentVariable = "AXIS_MCP_ROOT_CA_PATH";
    public const string ClientId = "axis_mcp";
    public const int CallbackPort = 48123;

    public required Uri ApiBaseUri { get; init; }
    public required Uri AuthorizationEndpoint { get; init; }
    public required Uri TokenEndpoint { get; init; }
    public required Uri RedirectUri { get; init; }
    public required string RootCertificatePath { get; init; }
    public required string AccessMode { get; init; }
    public bool MutationsEnabled => string.Equals(AccessMode, "write", StringComparison.Ordinal);
    public TimeSpan AuthorizationTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public static AxisMcpOptions FromEnvironment()
    {
        string apiBaseUrl = Environment.GetEnvironmentVariable(ApiBaseUrlEnvironmentVariable)
            ?? "https://localhost:5281/";
        Uri apiBaseUri = ParseApiBaseUri(apiBaseUrl);

        string rootCertificatePath = Environment.GetEnvironmentVariable(RootCaPathEnvironmentVariable)
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".dev-certs", "rootCA.pem");
        string accessMode = Environment.GetEnvironmentVariable(AccessEnvironmentVariable)
            ?? "read";

        return Create(apiBaseUri, rootCertificatePath, accessMode);
    }

    public static AxisMcpOptions Create(
        Uri apiBaseUri,
        string rootCertificatePath,
        string accessMode = "read")
    {
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootCertificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessMode);

        if (!apiBaseUri.IsAbsoluteUri || apiBaseUri.Scheme != Uri.UriSchemeHttps || !apiBaseUri.IsLoopback)
            throw new ArgumentException("The MCP API base URL must be an HTTPS loopback URL.", nameof(apiBaseUri));

        string normalizedAccessMode = accessMode.Trim().ToLowerInvariant() switch
        {
            "read" => "read",
            "write" => "write",
            _ => throw new ArgumentException(
                "accessMode must be 'read' or 'write'.",
                nameof(accessMode)),
        };

        Uri normalizedBaseUri = new(apiBaseUri.ToString().TrimEnd('/') + "/");

        return new AxisMcpOptions
        {
            ApiBaseUri = normalizedBaseUri,
            AuthorizationEndpoint = new Uri(normalizedBaseUri, "connect/authorize"),
            TokenEndpoint = new Uri(normalizedBaseUri, "connect/token"),
            RedirectUri = new Uri($"http://127.0.0.1:{CallbackPort.ToString(CultureInfo.InvariantCulture)}/callback"),
            RootCertificatePath = Path.GetFullPath(rootCertificatePath),
            AccessMode = normalizedAccessMode,
        };
    }

    private static Uri ParseApiBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            throw new ArgumentException($"{ApiBaseUrlEnvironmentVariable} must be an absolute URI.");

        return uri;
    }
}
