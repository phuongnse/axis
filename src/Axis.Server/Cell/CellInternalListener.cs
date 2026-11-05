using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Axis.Tenancy;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace Axis.Server.CellApi;

/// <summary>
/// The mutual TLS of a cell's internal listener, the Kestrel endpoint <c>Internal</c>. With <c>Cell:InternalClientCa</c> set,
/// that endpoint requires a client certificate that chains to the certificate authority in the PEM file. The public
/// listeners never ask for one. Revocation is not checked, because the region's internal authority publishes no revocation
/// list, and a certificate whose extended key usage leaves out client authentication is refused. The server alone reads the
/// setting, because only the server has listeners.
/// </summary>
internal sealed class CellInternalListener
{
    /// <summary>The Kestrel endpoint of the internal listener.</summary>
    public const string EndpointName = "Internal";

    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private const string AuthoritySetting = "Cell:InternalClientCa";

    private const string UrlSetting = "Cell:InternalUrl";

    private const string EndpointSetting = "Kestrel:Endpoints:Internal";

    private readonly X509Certificate2Collection _authorities;

    private CellInternalListener(X509Certificate2Collection authorities)
    {
        _authorities = authorities;
    }

    /// <summary>
    /// The listener of a cell with <c>Cell:InternalClientCa</c>, or null for a host that is not in a cell or has no such
    /// setting.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Cell:InternalClientCa</c> is set and its file cannot be read, <c>Cell:InternalUrl</c> is blank or not https, or the
    /// <c>Internal</c> endpoint is missing or not https. The message names each setting and never a value.
    /// </exception>
    public static CellInternalListener? Load(IConfiguration configuration, CellOptions cell)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(cell);

        if (!cell.IsCell || string.IsNullOrWhiteSpace(cell.InternalClientCa))
        {
            return null;
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(cell.InternalUrl)
            || !Uri.TryCreate(cell.InternalUrl.Trim(), UriKind.Absolute, out var internalUrl)
            || internalUrl.Scheme != Uri.UriSchemeHttps)
        {
            problems.Add($"The setting {UrlSetting} must be an https URL when {AuthoritySetting} is set.");
        }

        // Kestrel also takes https://+:port and https://*:port, which are not absolute URLs.
        var endpointUrl = configuration[$"{EndpointSetting}:Url"];
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            problems.Add($"The setting {EndpointSetting}:Url is required when {AuthoritySetting} is set.");
        }
        else if (!endpointUrl.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"The setting {EndpointSetting}:Url must be an https URL when {AuthoritySetting} is set.");
        }

        var authorities = new X509Certificate2Collection();
        try
        {
            authorities.ImportFromPemFile(cell.InternalClientCa.Trim());
        }
        catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
        {
            authorities.Clear();
        }

        if (authorities.Count == 0)
        {
            problems.Add($"The setting {AuthoritySetting} does not name a valid PEM certificate file.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"The cell's internal listener is invalid. {string.Join(" ", problems)}");
        }

        return new CellInternalListener(authorities);
    }

    /// <summary>Makes the endpoint require a client certificate of the region's internal authority.</summary>
    public void Apply(HttpsConnectionAdapterOptions https)
    {
        ArgumentNullException.ThrowIfNull(https);

        https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        https.ClientCertificateValidation = (certificate, _, _) => Trusts(certificate);
    }

    private bool Trusts(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.AddRange(_authorities);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));
        return chain.Build(certificate);
    }
}
