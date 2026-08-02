using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Mcp.Authentication;

namespace Axis.Mcp.Api;

public sealed class AxisApiClient(
    HttpClient httpClient,
    IAxisAccessTokenProvider accessTokenProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Task<string> GetJsonAsync(string path, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Get, path, body: null, cancellationToken);

    public Task<string> PostJsonAsync(string path, object body, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Post, path, body, cancellationToken);

    public Task<string> PutJsonAsync(string path, object body, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Put, path, body, cancellationToken);

    public Task<string> DeleteJsonAsync(string path, CancellationToken cancellationToken) =>
        SendJsonAsync(HttpMethod.Delete, path, body: null, cancellationToken);

    private async Task<string> SendJsonAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        Uri requestUri = BuildRelativeUri(path);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string accessToken = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
            using HttpRequestMessage request = new(method, requestUri);
            request.Headers.Authorization = new("Bearer", accessToken);
            request.Headers.Accept.ParseAdd("application/json");
            if (body is not null)
                request.Content = JsonContent.Create(body, options: JsonOptions);

            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                accessTokenProvider.Invalidate();
                continue;
            }

            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateApiException(response, responseBody);

            return responseBody;
        }

        throw new InvalidOperationException("Axis MCP exhausted its authentication retry.");
    }

    private Uri BuildRelativeUri(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _))
            throw new ArgumentException("Axis MCP API paths must be non-empty relative paths.", nameof(path));

        return new Uri(httpClient.BaseAddress!, path.TrimStart('/'));
    }

    private static AxisApiException CreateApiException(HttpResponseMessage response, string responseBody)
    {
        string message = $"Axis API returned HTTP {(int)response.StatusCode} {response.StatusCode}.";

        if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length <= 1000)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBody);
                string? title = document.RootElement.TryGetProperty("title", out JsonElement titleElement)
                    ? titleElement.GetString()
                    : null;
                string? detail = document.RootElement.TryGetProperty("detail", out JsonElement detailElement)
                    ? detailElement.GetString()
                    : null;
                string? description = string.Join(
                    " ",
                    new[] { title, detail }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (!string.IsNullOrWhiteSpace(description))
                    message = $"{message} {description}";
            }
            catch (JsonException)
            {
                // Keep the protocol error bounded and do not echo arbitrary server output.
            }
        }

        return new AxisApiException((int)response.StatusCode, message);
    }
}
