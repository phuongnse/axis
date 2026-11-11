using System.Text.Json;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Telemetry;
using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// Runs the recurring <c>applyPaymentWebhooks</c> job: it applies the events that wait in <c>billing.webhook_inbox</c>, see <c>docs/reference/payments.md#webhooks</c>. It takes
/// all of them in one order, the provider's time of the transfer, then the arrival, so the events of one invoice stay in order. Each event commits in its own transaction:
/// the row is locked, <see cref="Payments.RecordAsync"/> writes a <c>payment</c> ledger entry with the key <c>payos:</c> and the bank reference, the <c>payment.recorded</c> audit
/// record follows, and the row is marked <c>applied</c>. An event that does not fit its invoice is marked <c>outOfPlace</c> with its reason, writes nothing to the ledger, logs an
/// error and adds one to <see cref="ControlPlaneMetrics.PaymentsOutOfPlace"/>. The reasons are <c>unknownOrder</c>, <c>voidInvoice</c>, <c>notPaid</c> and <c>currency</c>. Any other
/// error stops the run, so the job queue retries it and a later event never overtakes an earlier one. The log never holds the body, an amount or an account. The payload is not used.
/// </summary>
internal sealed partial class PaymentWebhookEvents(NpgsqlDataSource controlPlane, TimeProvider clock, ILogger<PaymentWebhookEvents> logger) : IJobHandler
{
    public const string JobKind = "applyPaymentWebhooks";

    private const string Paid = "00";

    private const string DefaultCurrency = "VND";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var waiting = new List<Guid>();
        await using (var select = controlPlane.CreateCommand(
            "SELECT id FROM billing.webhook_inbox WHERE outcome IS NULL ORDER BY COALESCE(occurred_at, received_at), received_at, id"))
        {
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                waiting.Add(reader.GetGuid(0));
            }
        }

        foreach (var id in waiting)
        {
            await ApplyAsync(id, cancellationToken);
        }
    }

    private async Task ApplyAsync(Guid inboxId, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string body;
            await using (var select = new NpgsqlCommand("SELECT body FROM billing.webhook_inbox WHERE id = @id AND outcome IS NULL FOR UPDATE", connection, transaction))
            {
                select.Parameters.AddWithValue("id", inboxId);
                if (await select.ExecuteScalarAsync(cancellationToken) is not string text)
                {
                    return;
                }

                body = text;
            }

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.GetProperty("data");
            var orderCode = data.GetProperty("orderCode").GetInt64();
            var amount = data.GetProperty("amount").GetInt64();
            var reference = data.GetProperty("reference").GetString()!;
            var currency = data.TryGetProperty("currency", out var currencyElement) && currencyElement.ValueKind == JsonValueKind.String
                ? currencyElement.GetString()!
                : DefaultCurrency;
            var succeeded = data.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() == Paid;

            Guid? invoiceId = null;
            string? invoiceStatus = null;
            await using (var select = new NpgsqlCommand(
                "SELECT i.id, i.status FROM billing.payment_links l JOIN billing.invoices i ON i.id = l.invoice_id WHERE l.order_code = @code",
                connection,
                transaction))
            {
                select.Parameters.AddWithValue("code", orderCode);
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    invoiceId = reader.GetGuid(0);
                    invoiceStatus = reader.GetString(1);
                }
            }

            var reason = invoiceId is null ? "unknownOrder" : invoiceStatus == "void" ? "voidInvoice" : !succeeded || amount <= 0 ? "notPaid" : null;
            if (reason is null)
            {
                try
                {
                    if (await Payments.RecordAsync(transaction, invoiceId!.Value, amount, currency, reference, $"payos:{reference}", cancellationToken))
                    {
                        await AuditRecords.AppendAsync(transaction, AuditActorKind.Webhook, inboxId.ToString(), "payment.recorded", invoiceId.Value.ToString(), cancellationToken);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The currency is not the subscription's. Nothing is written, so the transaction ends and the row is marked in a new one.
                    await transaction.RollbackAsync(cancellationToken);
                    await transaction.DisposeAsync();
                    transaction = await connection.BeginTransactionAsync(cancellationToken);
                    reason = "currency";
                }
            }

            await MarkAsync(connection, transaction, inboxId, reason is null ? "applied" : "outOfPlace", reason, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (reason is not null)
            {
                LogOutOfPlace(inboxId, reason);
                ControlPlaneMetrics.RecordPaymentOutOfPlace(PaymentWebhookEndpoints.PayOsProvider, reason);
            }
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    private async Task MarkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid inboxId, string outcome, string? reason, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand(
            "UPDATE billing.webhook_inbox SET outcome = @outcome, reason = @reason, processed_at = @now WHERE id = @id AND outcome IS NULL",
            connection,
            transaction);
        update.Parameters.AddWithValue("outcome", outcome);
        update.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        update.Parameters.AddWithValue("now", clock.GetUtcNow());
        update.Parameters.AddWithValue("id", inboxId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Error, "The PayOS event {InboxId} does not fit its invoice ({Reason}). It is kept and applies nothing.")]
    private partial void LogOutOfPlace(Guid inboxId, string reason);
}
