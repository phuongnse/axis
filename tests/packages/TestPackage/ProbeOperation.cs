using System.Net.Http.Headers;
using Axis.Extensions;

namespace TestPackage;

/// <summary>
/// Uses the capabilities of its context in the way <c>mode</c> says and returns the <c>status</c> it
/// got. <c>declared</c> posts to <c>probe</c> through the declared <c>TestUrl</c> client with the
/// declared <c>TestToken</c> and the operation identity as the idempotency key. <c>otherHost</c> sends
/// a request to a host that is not declared. <c>undeclaredSecret</c> reads a secret that is not
/// declared. <c>swallow</c> does the same and catches the exception. The modes that start with <c>own</c> use a
/// <c>new HttpClient()</c> instead of the context's client, as a library does, and the address of the declared
/// <c>TestUrl</c>. <c>ownClient</c> posts to <c>probe</c> like <c>declared</c>. <c>ownOtherHost</c> sends a
/// request to <c>localhost</c> in place of the declared host. <c>ownRedirect</c> gets <c>probe</c> and follows
/// the redirect it is answered with.
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
            case "ownClient":
                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(context.GetHttpClient("TestUrl").BaseAddress!, "probe")) { Content = new StringContent("{}") })
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.GetSecret("TestToken"));
                    request.Headers.Add("Idempotency-Key", context.OperationId.ToString("D"));
                    using var response = await client.SendAsync(request, cancellationToken);
                    status = (long)response.StatusCode;
                }

                break;
            case "ownOtherHost":
                using (var client = new HttpClient())
                {
                    var declared = context.GetHttpClient("TestUrl").BaseAddress!;
                    var other = new UriBuilder(new Uri(declared, "probe")) { Host = "localhost" }.Uri;
                    using var response = await client.GetAsync(other, cancellationToken);
                    status = (long)response.StatusCode;
                }

                break;
            case "ownRedirect":
                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(context.GetHttpClient("TestUrl").BaseAddress!, "probe")))
                {
                    request.Headers.Add("Idempotency-Key", context.OperationId.ToString("D"));
                    using var response = await client.SendAsync(request, cancellationToken);
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
