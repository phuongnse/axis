using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;

namespace Axis.Admin;

/// <summary>Asks for the TLS certificate of a verified tenant host, so that tests can answer in place of the cluster.</summary>
internal interface ICertificateWriter
{
    /// <summary>
    /// Writes the certificate of <paramref name="host"/> for <paramref name="tenantId"/>. A certificate that already exists is success.
    /// Any other failure throws.
    /// </summary>
    Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken);
}

/// <summary>
/// The writer of the product. It creates a cert-manager <c>Certificate</c> through the Kubernetes API, named after the host, with the
/// secret of the same name and the HTTP-01 issuer of the cell. The name is what makes a second create a <c>409</c>, which counts as done.
/// The token is read on every call, because the cluster rotates it. A failure names the status only, never the token.
/// </summary>
internal sealed class KubernetesCertificateWriter(HttpClient http, Uri apiServer, string @namespace, Func<string> token, string issuer) : ICertificateWriter
{
    private const string AccountFolder = "/var/run/secrets/kubernetes.io/serviceaccount";

    /// <summary>
    /// The writer of the pod it runs in, which trusts the cluster's own certificate authority. It gives <c>null</c> outside a cluster:
    /// when the service host or port is not set, or the service account folder has no <c>ca.crt</c> or <c>namespace</c>.
    /// </summary>
    public static KubernetesCertificateWriter? InCluster(string issuer, string accountFolder = AccountFolder)
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var port = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");
        var authority = Path.Combine(accountFolder, "ca.crt");
        var namespaceFile = Path.Combine(accountFolder, "namespace");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port) || !File.Exists(authority) || !File.Exists(namespaceFile))
        {
            return null;
        }

        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(authority);
        var handler = new SocketsHttpHandler();
        handler.SslOptions.CertificateChainPolicy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust };
        handler.SslOptions.CertificateChainPolicy.CustomTrustStore.AddRange(roots);
        var tokenFile = Path.Combine(accountFolder, "token");
        return new KubernetesCertificateWriter(
            new HttpClient(handler, disposeHandler: true),
            new Uri($"https://{host}:{port}/"),
            File.ReadAllText(namespaceFile).Trim(),
            () => File.ReadAllText(tokenFile).Trim(),
            issuer);
    }

    public async Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken)
    {
        var body = new
        {
            apiVersion = "cert-manager.io/v1",
            kind = "Certificate",
            metadata = new { name = host, annotations = new Dictionary<string, string> { ["axis/tenant"] = tenantId } },
            spec = new
            {
                secretName = host,
                dnsNames = new[] { host },
                issuerRef = new { name = issuer, kind = "Issuer", group = "cert-manager.io" },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiServer, $"apis/cert-manager.io/v1/namespaces/{Uri.EscapeDataString(@namespace)}/certificates"))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token());
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The Kubernetes API answered {(int)response.StatusCode} to the certificate of a tenant host.", inner: null, response.StatusCode);
        }
    }
}
