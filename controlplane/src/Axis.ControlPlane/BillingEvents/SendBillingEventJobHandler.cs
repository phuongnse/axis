using System.Text;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using Axis.Processes.Instances;
using Npgsql;

namespace Axis.ControlPlane.BillingEvents;

/// <summary>
/// Runs a <c>sendBillingEvent</c> job: POSTs one event to <c>ControlPlane:BillingEvents:Url</c> as <c>{ type, id, timestamp, apiVersion, data }</c>
/// with the Standard Webhooks headers, see <c>docs/reference/billing-api.md#events</c>. The payload fixes the event's id, type, timestamp and
/// <c>data</c>. When it holds a <c>signUpId</c>, the job adds the business name and the administrator's name, email and language, which it reads
/// from the sign-up when it runs, as a payload is stored in the database and holds no personal data. The body is built in a fixed order, so every
/// attempt sends the same id and body, and only the timestamp header and the signature change. Any answer but 2xx throws, so the job runner retries
/// it and finally fails it. The exception names the status and never the address or the body, and nothing of the body is logged. The client does
/// not follow a redirect and times out after 10 seconds.
/// </summary>
internal sealed class SendBillingEventJobHandler(
    NpgsqlDataSource controlPlane,
    ControlPlaneOptions options,
    IHttpClientFactory httpClients,
    TimeProvider clock) : IJobHandler
{
    public const string HttpClientName = "billingEvents";

    public string Kind => BillingEventQueue.JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var id = Text(job, "id");
        var type = Text(job, "type");
        var timestamp = Text(job, "timestamp");
        if (job.Payload["data"] is not JsonObject payloadData)
        {
            throw new InvalidOperationException($"The {BillingEventQueue.JobKind} payload has no data.");
        }

        var data = (JsonObject)payloadData.DeepClone();
        if (job.Payload["signUpId"] is not null)
        {
            if (!Guid.TryParse(Text(job, "signUpId"), out var signUpId))
            {
                throw new InvalidOperationException($"The {BillingEventQueue.JobKind} payload has a signUpId that is not an id.");
            }

            await using var read = controlPlane.CreateCommand("SELECT business_name, full_name, email, language FROM control_plane.sign_ups WHERE id = @id");
            read.Parameters.AddWithValue("id", signUpId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"The sign-up of a {BillingEventQueue.JobKind} job does not exist.");
            }

            data["businessName"] = reader.GetString(0);
            data["administrator"] = new JsonObject { ["name"] = reader.GetString(1), ["email"] = reader.GetString(2), ["language"] = reader.GetString(3) };
        }

        // A fixed order, so every attempt sends the same bytes.
        var body = new JsonObject { ["type"] = type, ["id"] = id, ["timestamp"] = timestamp, ["apiVersion"] = "v1", ["data"] = data }.ToJsonString();
        var unixSeconds = clock.GetUtcNow().ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, options.BillingEvents.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("webhook-id", id);
        request.Headers.Add("webhook-timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add("webhook-signature", WebhookSignature.Sign(id, unixSeconds, body, options.BillingEvents.Secret!));

        using var client = httpClients.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The billing events receiver answered {(int)response.StatusCode}.");
        }
    }

    private static string Text(ClaimedJob job, string name) =>
        job.Payload[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrEmpty(text)
            ? text
            : throw new InvalidOperationException($"The {BillingEventQueue.JobKind} payload has no {name}.");
}
