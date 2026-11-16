using System.Net;
using System.Text.Json;
using Axis.Admin;

namespace Axis.Integration.Tests;

/// <summary>
/// The Kubernetes route writer posts one <c>networking.k8s.io/v1</c> Ingress for a host to the API server, with the bearer token of the
/// service account. The Ingress holds the class, the annotations, TLS with the host's secret and the <c>https</c> port of the server
/// Service. An existing Ingress (<c>409</c>) is success and any other status throws without the token. A delete sends <c>DELETE</c> to the
/// Ingress named after the host, where a <c>404</c> is done and any other failure throws without the token.
/// </summary>
public sealed class KubernetesRouteWriterTests
{
    private const string Backend = "nginx.ingress.kubernetes.io/backend-protocol";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_posts_an_ingress_for_the_host_with_the_class_the_annotations_the_tls_secret_and_the_https_port_of_the_server()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Created);
        var writer = Writer(handler, "nginx", new Dictionary<string, string> { [Backend] = "HTTPS" });

        await writer.WriteAsync("acme", "approvals.acme.vn", CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://10.0.0.1/apis/networking.k8s.io/v1/namespaces/axis/ingresses", request.Uri.ToString());
        Assert.Equal("Bearer token-1", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("networking.k8s.io/v1", root.GetProperty("apiVersion").GetString());
        Assert.Equal("Ingress", root.GetProperty("kind").GetString());
        var metadata = root.GetProperty("metadata");
        Assert.Equal("approvals.acme.vn", metadata.GetProperty("name").GetString());
        var annotations = metadata.GetProperty("annotations");
        Assert.Equal("acme", annotations.GetProperty("axis/tenant").GetString());
        Assert.Equal("HTTPS", annotations.GetProperty(Backend).GetString());
        var spec = root.GetProperty("spec");
        Assert.Equal("nginx", spec.GetProperty("ingressClassName").GetString());
        var tls = Assert.Single(spec.GetProperty("tls").EnumerateArray());
        Assert.Equal(["approvals.acme.vn"], tls.GetProperty("hosts").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Equal("approvals.acme.vn", tls.GetProperty("secretName").GetString());
        var rule = Assert.Single(spec.GetProperty("rules").EnumerateArray());
        Assert.Equal("approvals.acme.vn", rule.GetProperty("host").GetString());
        var path = Assert.Single(rule.GetProperty("http").GetProperty("paths").EnumerateArray());
        Assert.Equal("/", path.GetProperty("path").GetString());
        Assert.Equal("Prefix", path.GetProperty("pathType").GetString());
        var service = path.GetProperty("backend").GetProperty("service");
        Assert.Equal("axis-server", service.GetProperty("name").GetString());
        Assert.Equal("https", service.GetProperty("port").GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_blank_class_leaves_the_class_out_so_the_default_class_applies()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Created);

        await Writer(handler, ingressClass: null).WriteAsync("acme", "approvals.acme.vn", CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.False(body.RootElement.GetProperty("spec").TryGetProperty("ingressClassName", out _));
    }

    [Fact]
    public async Task A_configured_tenant_annotation_never_replaces_the_tenant_of_the_route()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Created);
        var writer = Writer(handler, "nginx", new Dictionary<string, string> { ["axis/tenant"] = "other" });

        await writer.WriteAsync("acme", "approvals.acme.vn", CancellationToken);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        Assert.Equal("acme", body.RootElement.GetProperty("metadata").GetProperty("annotations").GetProperty("axis/tenant").GetString());
    }

    [Fact]
    public async Task A_route_that_exists_counts_as_written()
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
    public async Task A_delete_sends_delete_with_the_token_to_the_ingress_named_after_the_host()
    {
        using var handler = new RecordingHandler(HttpStatusCode.OK);

        await Writer(handler).DeleteAsync("approvals.acme.vn", CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://10.0.0.1/apis/networking.k8s.io/v1/namespaces/axis/ingresses/approvals.acme.vn", request.Uri.ToString());
        Assert.Equal("Bearer token-1", request.Authorization);
        Assert.Equal("", request.Body);
    }

    [Fact]
    public async Task A_route_that_is_gone_counts_as_deleted()
    {
        using var handler = new RecordingHandler(HttpStatusCode.NotFound);

        await Writer(handler).DeleteAsync("approvals.acme.vn", CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Any_other_status_of_a_delete_throws_and_the_message_never_holds_the_token()
    {
        using var handler = new RecordingHandler(HttpStatusCode.Forbidden);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => Writer(handler).DeleteAsync("approvals.acme.vn", CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-1", exception.Message, StringComparison.Ordinal);
    }

    private static KubernetesRouteWriter Writer(HttpMessageHandler handler, string? ingressClass = "nginx", IReadOnlyDictionary<string, string>? annotations = null) =>
        new(
            new KubernetesApi(new HttpClient(handler), new Uri("https://10.0.0.1:443/"), "axis", () => "token-1"),
            "axis-server",
            ingressClass,
            annotations ?? new Dictionary<string, string>());

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
