using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;

namespace Axis.Admin;

/// <summary>
/// The in-cluster Kubernetes client that the host writers share, see
/// <see href="docs/reference/cells.md#hosts-and-certificates">hosts and certificates</see>. It creates or deletes one object in the
/// namespace of the pod. The object that already exists (<c>409</c>) is success of a create and the object that is already gone (<c>404</c>) is success
/// of a delete, which is what makes a repeat safe. The token is read on every call, because the cluster rotates it. A failure names the status only,
/// never the token.
/// </summary>
internal sealed class KubernetesApi(HttpClient http, Uri apiServer, string @namespace, Func<string> token)
{
    private const string AccountFolder = "/var/run/secrets/kubernetes.io/serviceaccount";

    /// <summary>
    /// The client of the pod it runs in, which trusts the cluster's own certificate authority. It gives <c>null</c> outside a cluster:
    /// when the service host or port is not set, or the service account folder has no <c>ca.crt</c> or <c>namespace</c>.
    /// </summary>
    public static KubernetesApi? InCluster(string accountFolder = AccountFolder)
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
        return new KubernetesApi(
            new HttpClient(handler, disposeHandler: true),
            new Uri($"https://{host}:{port}/"),
            File.ReadAllText(namespaceFile).Trim(),
            () => File.ReadAllText(tokenFile).Trim());
    }

    /// <summary>
    /// Posts <paramref name="body"/> to <paramref name="resource"/> under <paramref name="apiPath"/> in the pod's namespace. A <c>409</c> returns.
    /// Any other failure throws, and <paramref name="what"/> names the object in its message.
    /// </summary>
    public async Task CreateAsync(string apiPath, string resource, object body, string what, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(apiPath, resource))
        {
            Content = JsonContent.Create(body, body.GetType()),
        };
        await SendAsync(request, HttpStatusCode.Conflict, what, cancellationToken);
    }

    /// <summary>
    /// Deletes <paramref name="name"/> from <paramref name="resource"/> under <paramref name="apiPath"/> in the pod's namespace. A <c>404</c> returns.
    /// Any other failure throws, and <paramref name="what"/> names the object in its message.
    /// </summary>
    public async Task DeleteAsync(string apiPath, string resource, string name, string what, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url(apiPath, resource, name));
        await SendAsync(request, HttpStatusCode.NotFound, what, cancellationToken);
    }

    private Uri Url(string apiPath, string resource, string? name = null) =>
        new(apiServer, $"{apiPath}/namespaces/{Uri.EscapeDataString(@namespace)}/{resource}{(name is null ? "" : $"/{Uri.EscapeDataString(name)}")}");

    private async Task SendAsync(HttpRequestMessage request, HttpStatusCode accepted, string what, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token());
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == accepted)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The Kubernetes API answered {(int)response.StatusCode} to the {what} of a tenant host.", inner: null, response.StatusCode);
        }
    }
}
