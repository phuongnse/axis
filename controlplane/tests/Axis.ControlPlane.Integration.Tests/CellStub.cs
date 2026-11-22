using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.ControlPlane.Integration.Tests;

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed record ChangeCall(string Path, string? Key);

internal sealed record PutCall(string Path, IReadOnlyDictionary<string, long> Set);

/// <summary>
/// Stands in for the cell <c>vn-1</c>: a token endpoint, the job route, <c>POST /api/cell/tenants/{id}/resume</c> and <c>PUT /api/cell/tenants/{id}/quotas</c>.
/// It records each call with its <c>Idempotency-Key</c>. While <see cref="FailResumes"/> is set a resume answers <c>500</c> and is still recorded.
/// </summary>
internal sealed class CellStub : HttpMessageHandler
{
    private readonly ConcurrentQueue<ChangeCall> _resumes = new();
    private readonly ConcurrentQueue<PutCall> _puts = new();
    private readonly ConcurrentQueue<string> _calls = new();

    public bool FailResumes { get; set; }

    public IReadOnlyList<ChangeCall> Resumes => [.. _resumes];

    public IReadOnlyList<PutCall> Puts => [.. _puts];

    /// <summary>Every call that changes a tenant, as the method and the path, in the order it came.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/token")
        {
            return Json(new { access_token = "token", expires_in = 3600, token_type = "Bearer" });
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/api/cell/jobs/", StringComparison.Ordinal))
        {
            return Json(new { id = Guid.Parse(path["/api/cell/jobs/".Length..]), kind = "job", tenantId = "any", state = "succeeded", createdAt = DateTimeOffset.UnixEpoch });
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/resume", StringComparison.Ordinal))
        {
            _calls.Enqueue($"POST {path}");
            _resumes.Enqueue(new ChangeCall(path, request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));
            return FailResumes
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Json(new { id = Guid.NewGuid(), kind = "resume", tenantId = "any", state = "queued", createdAt = DateTimeOffset.UnixEpoch }, HttpStatusCode.Accepted);
        }

        if (request.Method == HttpMethod.Put && path.EndsWith("/quotas", StringComparison.Ordinal))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var set = body.RootElement.GetProperty("set").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt64());
            _calls.Enqueue($"PUT {path}");
            _puts.Enqueue(new PutCall(path, set));
            return Json(new { quotas = set.ToDictionary(pair => pair.Key, pair => new { limit = pair.Value }) });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
}
