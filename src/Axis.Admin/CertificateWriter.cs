namespace Axis.Admin;

/// <summary>Asks for the TLS certificate of a verified tenant host and for its deletion, so that tests can answer in place of the cluster.</summary>
internal interface ICertificateWriter
{
    /// <summary>
    /// Writes the certificate of <paramref name="host"/> for <paramref name="tenantId"/>. A certificate that already exists is success.
    /// Any other failure throws.
    /// </summary>
    Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken);

    /// <summary>Deletes the certificate of <paramref name="host"/>. A certificate that is already gone is success. Any other failure throws.</summary>
    Task DeleteAsync(string host, CancellationToken cancellationToken);
}

/// <summary>
/// The writer of the product. It creates a cert-manager <c>Certificate</c> through the Kubernetes API, named after the host, with the
/// secret of the same name and the HTTP-01 issuer of the cell. The name is what makes a second create a <c>409</c>, which counts as done. It deletes
/// that <c>Certificate</c> by the same name, and a <c>404</c> counts as done.
/// </summary>
internal sealed class KubernetesCertificateWriter(KubernetesApi api, string issuer) : ICertificateWriter
{
    public Task WriteAsync(string tenantId, string host, CancellationToken cancellationToken)
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
        return api.CreateAsync("apis/cert-manager.io/v1", "certificates", body, "certificate", cancellationToken);
    }

    public Task DeleteAsync(string host, CancellationToken cancellationToken) =>
        api.DeleteAsync("apis/cert-manager.io/v1", "certificates", host, "certificate deletion", cancellationToken);
}
