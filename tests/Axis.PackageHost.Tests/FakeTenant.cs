using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Axis.PackageHost.Tests;

/// <summary>
/// A fake tenant and token endpoint for the <c>connect</c> command. The token endpoint gives <c>token-1</c>, <c>token-2</c> and so on. The
/// tenant answers a registration with <c>204</c> unless <see cref="RegistrationAnswer"/> is set, answers a take with the next queued answer
/// or <c>204</c>, and records each posted result with the bearer token of the request.
/// </summary>
internal sealed class FakeTenant : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _takes = new();
    private readonly ConcurrentQueue<PostedResult> _results = new();
    private int _tokenRequests;
    private int _takeRequests;
    private int _registrations;

    /// <summary>The answer to a registration, or <see langword="null"/> for <c>204</c>.</summary>
    public Func<HttpResponseMessage>? RegistrationAnswer { get; set; }

    /// <summary>The status of the token endpoint.</summary>
    public HttpStatusCode TokenStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>The status that a result answers.</summary>
    public HttpStatusCode ResultStatus { get; set; } = HttpStatusCode.NoContent;

    public int TokenRequests => Volatile.Read(ref _tokenRequests);

    public int TakeRequests => Volatile.Read(ref _takeRequests);

    public int Registrations => Volatile.Read(ref _registrations);

    /// <summary>The bearer token of each take request, in order.</summary>
    public ConcurrentQueue<string?> TakeTokens { get; } = new();

    public IReadOnlyCollection<PostedResult> Results => _results;

    /// <summary>The body of the last registration.</summary>
    public JsonNode? Registered { get; private set; }

    public void EnqueueTake(Func<HttpResponseMessage> answer) => _takes.Enqueue(answer);

    public void EnqueueCall(Guid lease, string operation, JsonObject input, TimeSpan leaseFor, string package = "test-package", string version = "1.0.0") =>
        EnqueueTake(() => Json(HttpStatusCode.OK, new JsonObject
        {
            ["leaseId"] = lease,
            ["package"] = package,
            ["version"] = version,
            ["operation"] = operation,
            ["operationId"] = Guid.NewGuid(),
            ["input"] = input,
            ["leaseUntil"] = DateTimeOffset.UtcNow.Add(leaseFor),
        }));

    public static HttpResponseMessage Json(HttpStatusCode status, JsonNode body) =>
        new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        if (uri.Host == ConnectTestSettings.TokenHost)
        {
            var number = Interlocked.Increment(ref _tokenRequests);
            return TokenStatus == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, new JsonObject { ["access_token"] = $"token-{number}", ["expires_in"] = 300 })
                : new HttpResponseMessage(TokenStatus);
        }

        var token = request.Headers.Authorization?.Parameter;
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
        var path = uri.AbsolutePath;
        if (path == "/api/package-runner/packages")
        {
            Interlocked.Increment(ref _registrations);
            Registered = body;
            return RegistrationAnswer?.Invoke() ?? new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        if (path == "/api/package-runner/calls/take")
        {
            Interlocked.Increment(ref _takeRequests);
            TakeTokens.Enqueue(token);
            return _takes.TryDequeue(out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        const string leases = "/api/package-runner/leases/";
        if (path.StartsWith(leases, StringComparison.Ordinal) && path.EndsWith("/result", StringComparison.Ordinal))
        {
            var lease = Guid.Parse(path[leases.Length..^"/result".Length]);
            _results.Enqueue(new PostedResult(lease, body!.AsObject(), token));
            return new HttpResponseMessage(ResultStatus);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>A result that the command posted to <paramref name="Lease"/>, with the bearer token of the request.</summary>
internal sealed record PostedResult(Guid Lease, JsonObject Body, string? Token);
