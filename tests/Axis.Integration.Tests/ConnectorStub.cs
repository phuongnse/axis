using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// An external system for connector call tests, over real HTTP on a free local port. It records
/// every request it gets on any path, and answers <c>200 {"orderNumber":"PO-1"}</c> unless a test
/// lines up the next answers. A test can also hold the next response until it releases it, or drop
/// the connection of the next request that is not a <c>GET</c>. The stub stores the key of each
/// such request it acts on, from the <c>Idempotency-Key</c> header or a UUID last path segment. A
/// <c>GET</c> with no answer lined up whose last path segment is a UUID is a lookup: it answers the
/// default answer when that key is stored, and <c>404</c> otherwise.
/// </summary>
public sealed class ConnectorStub : IAsyncDisposable
{
    private const string DefaultBody = """{"orderNumber":"PO-1"}""";

    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly Lock _lock = new();
    private readonly WebApplication _app;
    private readonly Queue<(int Status, string Body)> _next = new();
    private readonly ConcurrentDictionary<Guid, bool> _keys = new();
    private HeldResponse? _held;
    private bool _dropNext;

    private ConnectorStub(WebApplication app) => _app = app;

    /// <summary>The stub's base address, such as <c>http://127.0.0.1:41234</c>, with no trailing slash.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>The requests the stub got, in the order they arrived.</summary>
    public IReadOnlyCollection<StubRequest> Requests => _requests;

    public static async Task<ConnectorStub> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var stub = new ConnectorStub(app);
        app.Run(stub.HandleAsync);
        await app.StartAsync();
        stub.BaseUrl = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single().TrimEnd('/');
        return stub;
    }

    /// <summary>Sets the answer to the next request. Later requests get the default answer again.</summary>
    public void Respond(int status, string body) => RespondMany((status, body));

    /// <summary>
    /// Sets the answers to the next requests, one each in order, in place of any answers still lined
    /// up. Later requests get the default answer again.
    /// </summary>
    public void RespondMany(params (int Status, string Body)[] answers)
    {
        lock (_lock)
        {
            _next.Clear();
            foreach (var answer in answers)
            {
                _next.Enqueue(answer);
            }
        }
    }

    /// <summary>Holds the response to the next request until the returned handle is released.</summary>
    public HeldResponse Hold()
    {
        var held = new HeldResponse();
        lock (_lock)
        {
            _held = held;
        }

        return held;
    }

    /// <summary>
    /// Stores the next request that is not a <c>GET</c>, then sends the response headers and drops
    /// the connection before the body, so the caller cannot know what happened. The headers stop
    /// the client from sending the request again on its own.
    /// </summary>
    public void DropNext()
    {
        lock (_lock)
        {
            _dropNext = true;
        }
    }

    /// <summary>The requests that carried <paramref name="key"/> in the <c>Idempotency-Key</c> header or as their last path segment.</summary>
    public IReadOnlyList<StubRequest> RequestsFor(Guid key) =>
        [.. _requests.Where(request => request.IdempotencyKey == key.ToString("D") || PathKey(request.Path) == key)];

    /// <summary>The requests that carried <paramref name="key"/> and are not a <c>GET</c>, so they could create something.</summary>
    public IReadOnlyList<StubRequest> CreateRequestsFor(Guid key) =>
        [.. RequestsFor(key).Where(request => !HttpMethods.IsGet(request.Method))];

    /// <summary>The requests that carried <paramref name="idempotencyKey"/>.</summary>
    public IReadOnlyList<StubRequest> RequestsWithKey(Guid idempotencyKey) =>
        [.. _requests.Where(request => request.IdempotencyKey == idempotencyKey.ToString("D"))];

    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _held?.Release();
        }

        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        _requests.Enqueue(new StubRequest(
            context.Request.Method,
            context.Request.Path.Value ?? "",
            context.Request.Headers.Authorization.ToString(),
            context.Request.Headers["Idempotency-Key"].ToString(),
            body,
            context.Request.Headers["traceparent"].ToString(),
            context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget,
            context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase)));

        var get = HttpMethods.IsGet(context.Request.Method);
        var key = Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var headerKey) ? headerKey : PathKey(context.Request.Path.Value);
        bool drop;
        lock (_lock)
        {
            drop = _dropNext && !get;
            _dropNext &= !drop;
        }

        if (drop)
        {
            if (key is { } dropped)
            {
                _keys[dropped] = true;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = 100;
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            context.Abort();
            return;
        }

        (int Status, string Body)? next;
        HeldResponse? held;
        lock (_lock)
        {
            next = _next.TryDequeue(out var answer) ? answer : null;
            (held, _held) = (_held, null);
        }

        if (next is null && key is { } created && !get)
        {
            _keys[created] = true;
        }
        else if (next is null && get && PathKey(context.Request.Path.Value) is { } lookedUp && !_keys.ContainsKey(lookedUp))
        {
            next = (StatusCodes.Status404NotFound, "{}");
        }

        if (held is not null)
        {
            held.Arrive();
            await held.Released;
        }

        var (status, text) = next ?? (StatusCodes.Status200OK, DefaultBody);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(text, context.RequestAborted);
    }

    private static Guid? PathKey(string? path) =>
        Guid.TryParse(path?[(path.LastIndexOf('/') + 1)..], out var key) ? key : null;
}

/// <summary>
/// A request the stub got. <see cref="Authorization"/>, <see cref="IdempotencyKey"/> and <see cref="Traceparent"/>
/// are empty when the header was missing. <see cref="RawTarget"/> is the path and query exactly as sent,
/// still escaped, and <see cref="Headers"/> holds every request header, ignoring letter case.
/// </summary>
public sealed record StubRequest(
    string Method,
    string Path,
    string Authorization,
    string IdempotencyKey,
    string Body,
    string Traceparent = "",
    string RawTarget = "",
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>A response the stub holds until <see cref="Release"/>.</summary>
public sealed class HeldResponse
{
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the held request has arrived at the stub.</summary>
    public Task Arrived => _arrived.Task;

    internal Task Released => _released.Task;

    /// <summary>Lets the stub answer the held request.</summary>
    public void Release() => _released.TrySetResult();

    internal void Arrive() => _arrived.TrySetResult();
}
