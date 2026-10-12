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
/// sets the next answer. A test can also hold the next response until it releases it.
/// </summary>
public sealed class ConnectorStub : IAsyncDisposable
{
    private const string DefaultBody = """{"orderNumber":"PO-1"}""";

    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly Lock _lock = new();
    private readonly WebApplication _app;
    private (int Status, string Body)? _next;
    private HeldResponse? _held;

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
    public void Respond(int status, string body)
    {
        lock (_lock)
        {
            _next = (status, body);
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
            body));

        (int Status, string Body)? next;
        HeldResponse? held;
        lock (_lock)
        {
            (next, _next) = (_next, null);
            (held, _held) = (_held, null);
        }

        if (held is not null)
        {
            held.Arrive();
            await held.Released;
        }

        var (status, answer) = next ?? (StatusCodes.Status200OK, DefaultBody);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(answer, context.RequestAborted);
    }
}

/// <summary>A request the stub got. <see cref="Authorization"/> and <see cref="IdempotencyKey"/> are empty when the header was missing.</summary>
public sealed record StubRequest(string Method, string Path, string Authorization, string IdempotencyKey, string Body);

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
