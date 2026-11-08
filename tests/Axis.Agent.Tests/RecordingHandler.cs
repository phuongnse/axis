using System.Net;
using System.Text;

namespace Axis.Agent.Tests;

/// <summary>Captures each request and answers from a queue of canned answers, so that no test reaches a provider.</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _answers = new();

    public List<RecordedRequest> Requests { get; } = [];

    public RecordingHandler Answer(HttpStatusCode status, string body = "", Action<HttpResponseMessage>? configure = null)
    {
        _answers.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return response;
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
            body));
        return _answers.Dequeue()();
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body);
