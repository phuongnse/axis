using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>One request that the stub receiver took.</summary>
internal sealed record ReceivedRequest(HttpMethod Method, string? Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    /// <summary>
    /// The <c>webhook-signature</c> that the Standard Webhooks text gives, recomputed here with each key and not with the code under test: one
    /// <c>v1,</c> signature of <c>{id}.{timestamp}.{body}</c> for each key, joined by spaces, see <c>docs/reference/billing-api.md#events</c>.
    /// </summary>
    public string ExpectedSignature(params byte[][] keys)
    {
        var signed = Encoding.UTF8.GetBytes($"{Headers["webhook-id"]}.{Headers["webhook-timestamp"]}.{Body}");
        return string.Join(' ', keys.Select(key => "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, signed))));
    }
}

/// <summary>Stands in for the billing system's receiver: it records each request and answers the status it is given.</summary>
internal sealed class ReceiverStub(HttpStatusCode status) : HttpMessageHandler
{
    public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Enqueue(new ReceivedRequest(request.Method, request.RequestUri?.ToString(), headers, await request.Content!.ReadAsStringAsync(cancellationToken)));
        return new HttpResponseMessage(status);
    }
}
