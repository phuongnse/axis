using System.Globalization;
using System.Net;

namespace Axis.Agent.Models;

/// <summary>The HTTP call that both adapters share: the retries on a rate limit and the errors that hold no key.</summary>
internal static class ModelHttp
{
    private const int MaxAttempts = 4;
    private const int MaxRetryAfterSeconds = 60;

    /// <summary>Sends the request and returns the body of a successful answer, with the number of attempts it took.</summary>
    /// <remarks>
    /// Status 429 and 529 (overloaded) are tried again, up to four attempts in all, after the seconds of <c>Retry-After</c> or 1, 2 and 4 seconds.
    /// Any other error fails at once. The message of a failure holds the binding name and the status code only.
    /// </remarks>
    public static async Task<(string Body, int Attempts)> SendAsync(
        HttpClient client,
        Func<HttpRequestMessage> build,
        ModelBinding binding,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            HttpStatusCode status;
            TimeSpan? retryAfter;
            try
            {
                using var request = build();
                using var response = await client.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return (await response.Content.ReadAsStringAsync(cancellationToken), attempt);
                }

                status = response.StatusCode;
                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (HttpRequestException)
            {
                // The exception is not kept: its message can hold the address.
                throw new ModelCallException($"The model call of binding '{binding.Name}' could not reach the provider.");
            }

            if (!IsRateLimit(status) || attempt == MaxAttempts)
            {
                throw new ModelCallException(
                    string.Create(CultureInfo.InvariantCulture, $"The model call of binding '{binding.Name}' failed with status {(int)status}."));
            }

            var delay = retryAfter is { } asked
                ? TimeSpan.FromSeconds(Math.Clamp(asked.TotalSeconds, 0, MaxRetryAfterSeconds))
                : TimeSpan.FromSeconds(1 << (attempt - 1));
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    private static bool IsRateLimit(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or (HttpStatusCode)529;

    /// <summary>The output limit of a call, never above the binding's own.</summary>
    public static int OutputLimit(ModelCall call, ModelBinding binding) =>
        Math.Min(call.MaxOutputTokens ?? binding.MaxOutputTokens, binding.MaxOutputTokens);

    /// <summary>A failure to read the provider's answer. The message names the binding and never the body.</summary>
    public static ModelCallException Unreadable(ModelBinding binding) =>
        new($"The model call of binding '{binding.Name}' got an answer that cannot be read.");
}

internal static partial class ModelLog
{
    /// <summary>One line for each successful call. It holds the binding, the model and the tokens, never the key, the messages or the body.</summary>
    [LoggerMessage(LogLevel.Information, "Model call of binding '{Binding}' to model '{Model}' used {InputTokens} input and {OutputTokens} output tokens in {Attempts} attempt(s).")]
    public static partial void Called(ILogger logger, string binding, string model, int inputTokens, int outputTokens, int attempts);
}
