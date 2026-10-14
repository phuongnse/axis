using System.Net.Http.Headers;
using Axis.Extensions;

namespace TestPackage;

/// <summary>
/// Uses the capabilities of its context in the way <c>mode</c> says and returns the <c>status</c> it
/// got. <c>declared</c> posts to <c>probe</c> through the declared <c>TestUrl</c> client with the
/// declared <c>TestToken</c> and the operation identity as the idempotency key. <c>otherHost</c> sends
/// a request to a host that is not declared. <c>undeclaredSecret</c> reads a secret that is not
/// declared. <c>swallow</c> does the same and catches the exception.
/// </summary>
public sealed class ProbeOperation : IOperation
{
    public async Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);

        long status = 0;
        switch ((string?)input["mode"])
        {
            case "declared":
                using (var client = context.GetHttpClient("TestUrl"))
                using (var request = new HttpRequestMessage(HttpMethod.Post, "probe") { Content = new StringContent("{}") })
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.GetSecret("TestToken"));
                    request.Headers.Add("Idempotency-Key", context.OperationId.ToString("D"));
                    using var response = await client.SendAsync(request, cancellationToken);
                    status = (long)response.StatusCode;
                }

                break;
            case "otherHost":
                using (var client = context.GetHttpClient("TestUrl"))
                using (var response = await client.GetAsync(new Uri("https://other.example.test/probe"), cancellationToken))
                {
                    status = (long)response.StatusCode;
                }

                break;
            case "undeclaredSecret":
                _ = context.GetSecret("OtherToken");
                break;
            case "swallow":
                try
                {
                    _ = context.GetSecret("OtherToken");
                }
                catch (InvalidOperationException)
                {
                    // The platform still fails the call.
                }

                break;
            default:
                throw new ArgumentException("Unknown mode.", nameof(input));
        }

        return new Dictionary<string, object?> { ["status"] = status };
    }
}
