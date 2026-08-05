using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Axis.Mcp.Configuration;

public static class AxisMcpHttpClientFactory
{
    public static HttpClient Create(AxisMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        HttpClientHandler handler = new();
        X509Certificate2? rootCertificate = LoadRootCertificate(options.RootCertificatePath);

        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            ValidateServerCertificate(options.ApiBaseUri, rootCertificate, certificate, errors);

        return new HttpClient(handler)
        {
            BaseAddress = options.ApiBaseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    internal static bool ValidateServerCertificate(
        Uri requestUri,
        X509Certificate2? rootCertificate,
        X509Certificate2? certificate,
        SslPolicyErrors errors)
    {
        if (!requestUri.IsLoopback || requestUri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (errors == SslPolicyErrors.None)
            return true;

        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 ||
            (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0 ||
            certificate is null ||
            rootCertificate is null)
            return false;

        using X509Chain chain = new();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        return chain.Build(certificate);
    }

    private static X509Certificate2? LoadRootCertificate(string path)
    {
        if (!File.Exists(path))
            return null;

        return X509CertificateLoader.LoadCertificateFromFile(path);
    }
}
