using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Tenancy;

namespace Axis.Processes.Instances;

/// <summary>
/// Builds what a connector call keeps of the request it sent and of each response it got, as
/// <c>docs/reference/process-inspection.md#call-input-kept</c> says. A body is cut at
/// <see cref="BodyLimit"/> bytes, and <c>Authorization</c>, the header the connector's auth names and a
/// response's <c>Set-Cookie</c> are shown as <see cref="SecretValue.Placeholder"/>. The values are never
/// copied to history, audit records or logs.
/// </summary>
internal static class KeptExchange
{
    /// <summary>The most bytes of UTF-8 that a kept request body or response body holds: 64 KiB.</summary>
    public const int BodyLimit = 64 * 1024;

    /// <summary>
    /// Cuts <paramref name="text"/> to at most <see cref="BodyLimit"/> bytes of UTF-8, at the last whole
    /// character within the limit. <c>Truncated</c> is <see langword="true"/> when text was cut.
    /// </summary>
    public static (string Text, bool Truncated) Cut(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length <= BodyLimit)
        {
            return (text, false);
        }

        // A continuation byte (10xxxxxx) is inside a character, so step back to the character's first byte.
        var end = BodyLimit;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80)
        {
            end--;
        }

        return (Encoding.UTF8.GetString(bytes, 0, end), true);
    }

    /// <summary>
    /// The request as it is just before it is sent: its method, its <paramref name="url"/> without the
    /// query, the <paramref name="query"/> as name and value, the headers Axis sets, and the body text, or
    /// <see langword="null"/> when the request has none.
    /// </summary>
    public static JsonObject Request(
        HttpRequestMessage request,
        string url,
        IEnumerable<KeyValuePair<string, string>> query,
        string? body,
        ConnectorModel connector)
    {
        var kept = body is null ? (Text: (string?)null, Truncated: false) : Cut(body);
        var queryValues = new JsonObject();
        foreach (var pair in query)
        {
            queryValues[pair.Key] = pair.Value;
        }

        var headers = Headers(request.Headers, connector, false);
        if (request.Content is { } content)
        {
            foreach (var header in Headers(content.Headers, connector, false))
            {
                headers[header.Key] = header.Value?.DeepClone();
            }
        }

        return new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = url,
            ["query"] = queryValues,
            ["headers"] = headers,
            ["body"] = kept.Text,
            ["truncated"] = kept.Truncated,
        };
    }

    /// <summary>
    /// The response as the outside system answered: its status, its headers, and the body text with
    /// <paramref name="truncated"/> as the cut says, or <see langword="null"/> when the body could not be read.
    /// </summary>
    public static JsonObject Response(HttpResponseMessage response, string? body, bool truncated, ConnectorModel connector)
    {
        var headers = Headers(response.Headers, connector, true);
        foreach (var header in Headers(response.Content.Headers, connector, true))
        {
            headers[header.Key] = header.Value?.DeepClone();
        }

        return new JsonObject
        {
            ["status"] = (int)response.StatusCode,
            ["headers"] = headers,
            ["body"] = body,
            ["truncated"] = truncated,
        };
    }

    /// <summary>The headers as an object of name and joined values. A secret header holds the placeholder.</summary>
    private static JsonObject Headers(System.Net.Http.Headers.HttpHeaders headers, ConnectorModel connector, bool response)
    {
        var kept = new JsonObject();
        foreach (var header in headers)
        {
            kept[header.Key] = IsSecret(header.Key, connector, response) ? SecretValue.Placeholder : string.Join(", ", header.Value);
        }

        return kept;
    }

    private static bool IsSecret(string name, ConnectorModel connector, bool response) =>
        string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase)
        || (connector.Auth?.HeaderName is { } authHeader && string.Equals(name, authHeader, StringComparison.OrdinalIgnoreCase))
        || (response && string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase));
}
