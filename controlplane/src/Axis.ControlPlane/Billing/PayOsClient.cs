using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// The real PayOS, see <c>docs/reference/payments.md#paying-in-vietnam</c>. Each request carries the <c>x-client-id</c> and <c>x-api-key</c> headers, and a create
/// request also carries the HMAC-SHA256 signature of its fields made with the checksum key. Each answer is refused unless its <c>signature</c> is the HMAC-SHA256 of its
/// <c>data</c> made with the same key, built as the PayOS SDK builds it. The three keys come from the settings, which read them from Secret files. The adapter writes
/// no log of its own, and no message of its exceptions holds a key or the free text of a PayOS answer, because PayOS may echo a request back.
/// </summary>
internal sealed partial class PayOsClient(HttpClient http, ControlPlaneOptions options) : IPayOs
{
    private const string Success = "00";

    private static readonly JsonSerializerOptions _camelCase = new(JsonSerializerDefaults.Web);

    private static readonly JsonWriterOptions _relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _baseUrl = (options.PayOs.BaseUrl ?? throw new InvalidOperationException("The PayOS base URL is not set.")).TrimEnd('/');
    private readonly string _clientId = options.PayOs.ClientId ?? throw new InvalidOperationException("The PayOS client id is not set.");
    private readonly string _apiKey = options.PayOs.ApiKey ?? throw new InvalidOperationException("The PayOS API key is not set.");
    private readonly byte[] _checksumKey = Encoding.UTF8.GetBytes(options.PayOs.ChecksumKey ?? throw new InvalidOperationException("The PayOS checksum key is not set."));

    public async Task<PayOsPaymentLink> CreatePaymentLinkAsync(PayOsPaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // PayOS requires both addresses and signs them. The billing portal page that should receive the payer is planned, so both are the public URL.
        var returnUrl = options.PublicUrl ?? throw new InvalidOperationException("The control plane's public URL is not set.");
        var signature = Sign($"amount={Number(request.Amount)}&cancelUrl={returnUrl}&description={request.Description}&orderCode={Number(request.OrderCode)}&returnUrl={returnUrl}");
        var body = JsonSerializer.Serialize(
            new CreateBody(request.OrderCode, request.Amount, request.Description, returnUrl, returnUrl, signature),
            _camelCase);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/payment-requests")
        {
            Content = new StringContent(body, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json")),
        };

        var data = await SendAsync(message, cancellationToken);
        return Read(() => new PayOsPaymentLink(
            Text(data, "paymentLinkId"),
            Text(data, "checkoutUrl"),
            Text(data, "qrCode"),
            Text(data, "bin"),
            Text(data, "accountNumber"),
            Text(data, "accountName"),
            Text(data, "description")));
    }

    public async Task<PayOsPaymentStatus> GetPaymentStatusAsync(long orderCode, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v2/payment-requests/{Number(orderCode)}");
        var data = await SendAsync(message, cancellationToken);
        return Read(() => new PayOsPaymentStatus(
            data.GetProperty("orderCode").GetInt64(),
            Text(data, "status"),
            data.GetProperty("amount").GetInt64(),
            data.GetProperty("amountPaid").GetInt64()));
    }

    // Sends the request, refuses an error answer and an answer with a wrong signature, and returns a copy of the answer's data, which outlives the parsed document.
    private async Task<JsonElement> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        message.Headers.Add("x-client-id", _clientId);
        message.Headers.Add("x-api-key", _apiKey);

        string body;
        HttpStatusCode status;
        try
        {
            using var response = await http.SendAsync(message, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new PayOsException("PayOS could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PayOsException("PayOS did not answer in time.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new PayOsException(ErrorMessage(status, null));
        }

        using (document)
        {
            var root = document.RootElement;
            var code = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()
                : null;
            if ((int)status is < 200 or >= 300 || code != Success)
            {
                throw new PayOsException(ErrorMessage(status, code));
            }

            if (!root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("signature", out var signature)
                || signature.ValueKind != JsonValueKind.String
                || !IsSigned(data, signature.GetString()!))
            {
                throw new PayOsException("PayOS answered without a valid signature.");
            }

            return data.Clone();
        }
    }

    // The status and PayOS's own code, and never its free text, which may echo the request. A code that is not short and plain is not trusted either.
    private static string ErrorMessage(HttpStatusCode status, string? code) =>
        $"PayOS refused the request with HTTP {(int)status} and code {(code is not null && PlainCode().IsMatch(code) ? code : "unknown")}.";

    private static T Read<T>(Func<T> map)
    {
        try
        {
            return map();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or FormatException or JsonException)
        {
            throw new PayOsException("PayOS answered with data that could not be read.");
        }
    }

    private static string Text(JsonElement data, string name) =>
        data.GetProperty(name).GetString() ?? throw new InvalidOperationException("A field is null.");

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private string Sign(string text) => Convert.ToHexStringLower(HMACSHA256.HashData(_checksumKey, Encoding.UTF8.GetBytes(text)));

    private bool IsSigned(JsonElement data, string signature) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Sign(Canonical(data))),
            Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

    // The form that PayOS's SDK signs (sortObjDataByKey and convertObjToQueryStr): the keys in ordinal order as key=value joined by &. A null, "null" or "undefined" is empty,
    // a string is as it is, a number or a bool is its JSON text, and an array is compact JSON with the keys of each of its objects in ordinal order.
    private static string Canonical(JsonElement data)
    {
        var parts = new List<string>();
        foreach (var property in data.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Array => SortedArray(property.Value),
                _ => property.Value.GetRawText(),
            };
            if (value is "null" or "undefined")
            {
                value = string.Empty;
            }

            parts.Add($"{property.Name}={value}");
        }

        return string.Join('&', parts);
    }

    private static string SortedArray(JsonElement array)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _relaxed))
        {
            writer.WriteStartArray();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    item.WriteTo(writer);
                    continue;
                }

                writer.WriteStartObject();
                foreach (var property in item.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [GeneratedRegex("^[0-9A-Za-z_-]{1,16}$")]
    private static partial Regex PlainCode();

    private sealed record CreateBody(long OrderCode, long Amount, string Description, string CancelUrl, string ReturnUrl, string Signature);
}
