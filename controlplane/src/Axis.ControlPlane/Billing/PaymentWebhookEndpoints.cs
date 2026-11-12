using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// The payment webhook routes, see <c>docs/reference/payments.md#webhooks</c>. They are mapped only when <c>ControlPlane:PayOs:Provider</c> is set. A route checks the
/// provider's checksum over the body before it trusts any field, keeps the event once in <c>billing.webhook_inbox</c> and leaves its application to
/// <see cref="PaymentWebhookEvents"/>.
/// </summary>
internal static partial class PaymentWebhookEndpoints
{
    public const string PayOsProvider = "payos";

    private const string TransactionTimeFormat = "yyyy-MM-dd HH:mm:ss";

    // PayOS writes the time of a transfer in Vietnam time and gives no offset.
    private static readonly TimeSpan _vietnamOffset = TimeSpan.FromHours(7);

    public static void MapPaymentWebhookEndpoints(this IEndpointRouteBuilder endpoints) =>
        // A missing key, a body that is not JSON and a wrong checksum are all 401 and log no part of the body. A signed event with no orderCode, amount or reference
        // answers 400 and is not stored, so PayOS retries it. A repeated event answers 200 and changes nothing.
        endpoints.MapPost("/api/payments/payos/webhook", PayOsAsync);

    /// <summary>Reads the Vietnam time of a PayOS transfer as UTC, or <see langword="null"/> when it is missing or not in PayOS's form.</summary>
    internal static DateTimeOffset? ParseTransactionTime(string? text) =>
        DateTime.TryParseExact(text, TransactionTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, _vietnamOffset).ToUniversalTime()
            : null;

    private static async Task<IResult> PayOsAsync(
        HttpRequest request,
        ControlPlaneOptions options,
        NpgsqlDataSource controlPlane,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        string body;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var logger = loggers.CreateLogger(typeof(PaymentWebhookEndpoints).FullName!);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            LogRefused(logger);
            return Results.Unauthorized();
        }

        using (document)
        {
            var root = document.RootElement;
            if (string.IsNullOrEmpty(options.PayOs.ChecksumKey)
                || root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("signature", out var signature)
                || signature.ValueKind != JsonValueKind.String
                || !PayOsSignature.IsSigned(Encoding.UTF8.GetBytes(options.PayOs.ChecksumKey), data, signature.GetString()!))
            {
                LogRefused(logger);
                return Results.Unauthorized();
            }

            if (!data.TryGetProperty("reference", out var reference)
                || reference.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(reference.GetString())
                || !data.TryGetProperty("orderCode", out var orderCode)
                || !orderCode.TryGetInt64(out var orderCodeValue)
                || !data.TryGetProperty("amount", out var amount)
                || !amount.TryGetInt64(out _))
            {
                return Results.BadRequest();
            }

            var occurredAt = data.TryGetProperty("transactionDateTime", out var time) && time.ValueKind == JsonValueKind.String
                ? ParseTransactionTime(time.GetString())
                : null;

            await using var insert = controlPlane.CreateCommand(
                """
                INSERT INTO billing.webhook_inbox (id, provider, event_id, order_code, occurred_at, body)
                VALUES (@id, @provider, @eventId, @orderCode, @occurredAt, @body)
                ON CONFLICT (provider, event_id) DO NOTHING
                """);
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("provider", PayOsProvider);
            insert.Parameters.AddWithValue("eventId", reference.GetString()!);
            insert.Parameters.AddWithValue("orderCode", orderCodeValue);
            insert.Parameters.AddWithValue("occurredAt", (object?)occurredAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("body", body);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return Results.Ok();
        }
    }

    [LoggerMessage(LogLevel.Warning, "A PayOS webhook was refused because its checksum did not match.")]
    private static partial void LogRefused(ILogger logger);
}
