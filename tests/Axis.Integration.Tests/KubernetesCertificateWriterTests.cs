using System.Net;
using System.Text.Json;
using Axis.Admin;

namespace Axis.Integration.Tests;

/// <summary>
/// The Kubernetes writer posts one cert-manager <c>Certificate</c> for a host to the API server, with the bearer token of the service
/// account, and deletes it by its name. An existing Certificate (<c>409</c>) is success of a write and a Certificate that is gone (<c>404</c>) is
/// success of a delete. Any other status throws without the token. Outside a cluster the in-cluster factory gives nothing.
/// </summary>
public sealed class KubernetesCertificateWriterTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_posts_a_certificate_for_the_host_only_with_the_bearer_token_and_the_http_01_issuer()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Created);
        var writer = Writer(handler);

        await writer.WriteAsync("acme", "approvals.acme.vn", CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://10.0.0.1/apis/cert-manager.io/v1/namespaces/axis/certificates", request.Uri.ToString());
        Assert.Equal("Bearer token-1", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("cert-manager.io/v1", root.GetProperty("apiVersion").GetString());
        Assert.Equal("Certificate", root.GetProperty("kind").GetString());
        Assert.Equal("approvals.acme.vn", root.GetProperty("metadata").GetProperty("name").GetString());
        Assert.Equal("acme", root.GetProperty("metadata").GetProperty("annotations").GetProperty("axis/tenant").GetString());
        var spec = root.GetProperty("spec");
        Assert.Equal("approvals.acme.vn", spec.GetProperty("secretName").GetString());
        Assert.Equal(["approvals.acme.vn"], spec.GetProperty("dnsNames").EnumerateArray().Select(item => item.GetString()).ToArray());
        var issuer = spec.GetProperty("issuerRef");
        Assert.Equal("axis-tenant-hosts", issuer.GetProperty("name").GetString());
        Assert.Equal("Issuer", issuer.GetProperty("kind").GetString());
        Assert.Equal("cert-manager.io", issuer.GetProperty("group").GetString());
    }

    [Fact]
    public async Task A_certificate_that_exists_counts_as_written()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Conflict);

        await Writer(handler).WriteAsync("acme", "approvals.acme.vn", CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Any_other_status_throws_and_the_message_never_holds_the_token()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Writer(handler).WriteAsync("acme", "approvals.acme.vn", CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_sends_the_bearer_token_to_the_url_of_the_certificate()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);

        await Writer(handler).DeleteAsync("approvals.acme.vn", CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://10.0.0.1/apis/cert-manager.io/v1/namespaces/axis/certificates/approvals.acme.vn", request.Uri.ToString());
        Assert.Equal("Bearer token-1", request.Authorization);
    }

    [Fact]
    public async Task A_certificate_that_is_gone_counts_as_deleted()
    {
        using var handler = new RecordingHandler(HttpStatusCode.NotFound);

        await Writer(handler).DeleteAsync("approvals.acme.vn", CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_refused_delete_throws_and_the_message_never_holds_the_token()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Forbidden);
        var writer = new KubernetesCertificateWriter(new KubernetesApi(new HttpClient(handler), new Uri("https://10.0.0.1:443/"), "axis", () => "token-4815162342"), "axis-tenant-hosts");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => writer.DeleteAsync("approvals.acme.vn", CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("4815162342", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_is_read_on_every_write()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Created);
        var tokens = new Queue<string>(["token-1", "token-2"]);
        var writer = new KubernetesCertificateWriter(new KubernetesApi(new HttpClient(handler), new Uri("https://10.0.0.1:443/"), "axis", tokens.Dequeue), "axis-tenant-hosts");

        await writer.WriteAsync("acme", "a.acme.vn", CancellationToken);
        await writer.WriteAsync("acme", "b.acme.vn", CancellationToken);

        Assert.Equal(["Bearer token-1", "Bearer token-2"], handler.Requests.Select(request => request.Authorization));
    }

    [Fact]
    public void A_folder_without_the_service_account_files_gives_no_client()
    {
        var folder = Directory.CreateTempSubdirectory("axis-service-account-");
        try
        {
            Assert.Null(KubernetesApi.InCluster(folder.FullName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static KubernetesCertificateWriter Writer(HttpMessageHandler handler) =>
        new(new KubernetesApi(new HttpClient(handler), new Uri("https://10.0.0.1:443/"), "axis", () => "token-1"), "axis-tenant-hosts");

    private sealed record Recorded(HttpMethod Method, Uri Uri, string? Authorization, string Body);

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private readonly List<Recorded> _requests = [];

        public IReadOnlyList<Recorded> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Add(new Recorded(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(status);
        }
    }
}
