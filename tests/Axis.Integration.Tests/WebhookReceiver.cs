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
/// An outside system for webhook delivery tests, over real HTTP on a free local port. It records every request it gets,
/// with its method, path, headers and raw body, and answers <c>200</c> unless a test lines up the next answers for a
/// path. An answer of <c>3xx</c> carries a <c>Location</c> that points back at the same path, so a client that followed
/// it would send a second request.
/// </summary>
public sealed class WebhookReceiver : IAsyncDisposable
{
    private readonly ConcurrentQueue<WebhookRequest> _requests = new();
    private readonly Dictionary<string, Queue<int>> _next = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly WebApplication _app;

    private WebhookReceiver(WebApplication app) => _app = app;

    /// <summary>The receiver's base address, such as <c>http://127.0.0.1:41234</c>, with no trailing slash.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>The requests the receiver got, in the order they arrived.</summary>
    public IReadOnlyCollection<WebhookRequest> Requests => _requests;

    public static async Task<WebhookReceiver> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var receiver = new WebhookReceiver(app);
        app.Run(receiver.HandleAsync);
        await app.StartAsync();
        receiver.BaseUrl = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single().TrimEnd('/');
        return receiver;
    }

    /// <summary>The requests to <paramref name="path"/>, in the order they arrived.</summary>
    public IReadOnlyList<WebhookRequest> RequestsTo(string path) =>
        [.. _requests.Where(request => string.Equals(request.Path, path, StringComparison.Ordinal))];

    /// <summary>Sets the statuses of the next requests to <paramref name="path"/>, one each in order. Later requests get <c>200</c>.</summary>
    public void Respond(string path, params int[] statuses)
    {
        lock (_lock)
        {
            _next[path] = new Queue<int>(statuses);
        }
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    private async Task HandleAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var path = context.Request.Path.Value ?? "";
        _requests.Enqueue(new WebhookRequest(
            context.Request.Method,
            path,
            context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            body));
        int status;
        lock (_lock)
        {
            status = _next.TryGetValue(path, out var queue) && queue.TryDequeue(out var next) ? next : StatusCodes.Status200OK;
        }

        context.Response.StatusCode = status;
        if (status is >= 300 and < 400)
        {
            context.Response.Headers.Location = path;
        }
    }
}

/// <summary>A request the receiver got. <see cref="Headers"/> holds every request header, ignoring letter case.</summary>
public sealed record WebhookRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body);
