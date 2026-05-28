using System.Text.Json;
using Axis.WorkflowEngine.Application.Services;
using Microsoft.Extensions.Logging;

namespace Axis.WorkflowEngine.Infrastructure.Services.StepExecutors;

/// <summary>
/// HTTP Request step executor. Reads method/url/headers/body/auth/timeout from stepConfig,
/// interpolates context expressions, and executes the outbound HTTP call.
/// Temporary implementation that returns a structured placeholder response.
/// </summary>
internal sealed class HttpStepExecutor(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpStepExecutor> logger)
    : IHttpStepExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?>? stepConfig,
        IReadOnlyDictionary<string, object?> context,
        CancellationToken ct = default)
    {
        if (stepConfig is null)
            throw new InvalidOperationException("HTTP step config is required.");

        string method = (GetString(stepConfig, "method") ?? "GET").ToUpperInvariant();
        string? urlTemplate = GetString(stepConfig, "url");
        if (string.IsNullOrWhiteSpace(urlTemplate))
            throw new InvalidOperationException("HTTP step 'url' is required.");

        string url = InterpolateExpression(urlTemplate, context);
        int timeoutSeconds = GetInt(stepConfig, "timeout") ?? 30;
        string outputVariable = GetString(stepConfig, "outputVariable") ?? "http_response";

        using HttpClient client = httpClientFactory.CreateClient("StepExecutor");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        using HttpRequestMessage request = new(new HttpMethod(method), url);

        // Apply headers
        if (stepConfig.TryGetValue("headers", out object? headersRaw)
            && headersRaw is JsonElement { ValueKind: JsonValueKind.Object } headersJson)
        {
            foreach (JsonProperty header in headersJson.EnumerateObject())
                request.Headers.TryAddWithoutValidation(header.Name, InterpolateExpression(header.Value.ToString(), context));
        }

        // Apply body for POST/PUT/PATCH
        if (method is "POST" or "PUT" or "PATCH"
            && stepConfig.TryGetValue("body", out object? bodyRaw) && bodyRaw is not null)
        {
            string bodyStr = bodyRaw is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(bodyRaw, JsonOptions);
            bodyStr = InterpolateExpression(bodyStr, context);
            request.Content = new StringContent(bodyStr, System.Text.Encoding.UTF8, "application/json");
        }

        logger.LogInformation(
            "HTTP step executing {Method} {Url}", method.ToUpperInvariant(), url);

        HttpResponseMessage response = await client.SendAsync(request, ct);
        string responseBody = await response.Content.ReadAsStringAsync(ct);

        // Truncate large response bodies
        const int MaxBodyBytes = 1_048_576;
        if (System.Text.Encoding.UTF8.GetByteCount(responseBody) > MaxBodyBytes)
        {
            responseBody = responseBody[..MaxBodyBytes] + "...[truncated]";
            logger.LogWarning("HTTP step response body truncated at 1 MB for execution context.");
        }

        object? parsedBody = null;
        try
        {
            parsedBody = JsonSerializer.Deserialize<object>(responseBody, JsonOptions);
        }
        catch
        {
            parsedBody = responseBody;
        }

        Dictionary<string, object?> output = new()
        {
            [outputVariable] = new Dictionary<string, object?>
            {
                ["status_code"] = (int)response.StatusCode,
                ["body"] = parsedBody,
                ["headers"] = response.Headers.ToDictionary(h => h.Key, h => (object?)string.Join(", ", h.Value))
            }
        };

        return output;
    }

    private static string InterpolateExpression(string template, IReadOnlyDictionary<string, object?> context)
    {
        // Simple {{key}} interpolation — replaces {{key}} and {{step.field}} patterns
        return System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{context\.([^}]+)\}\}",
            match =>
            {
                string path = match.Groups[1].Value;
                string[] parts = path.Split('.', 2);
                if (parts.Length == 1 && context.TryGetValue(parts[0], out object? val))
                    return val?.ToString() ?? string.Empty;
                if (parts.Length == 2 && context.TryGetValue(parts[0], out object? parent)
                    && parent is JsonElement je && je.TryGetProperty(parts[1], out JsonElement field))
                    return field.ToString();
                return string.Empty;
            });
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out object? raw) || raw is null) return null;
        return raw is JsonElement je ? je.ToString() : raw.ToString();
    }

    private static int? GetInt(IReadOnlyDictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out object? raw) || raw is null) return null;
        if (raw is JsonElement { ValueKind: JsonValueKind.Number } je && je.TryGetInt32(out int v)) return v;
        return int.TryParse(raw.ToString(), out int r) ? r : null;
    }
}
