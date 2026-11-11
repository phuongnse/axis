using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// Runs the recurring <c>createPaymentLinks</c> job: it gives each <c>open</c> <c>VND</c> invoice one PayOS payment link, see
/// <c>docs/reference/payments.md#paying-in-vietnam</c>. A paid, <c>void</c> or <c>USD</c> invoice gets none. The job saves the invoice's
/// <c>orderCode</c>, from the sequence <c>billing.payment_order_codes</c>, in its own transaction before it calls PayOS, so a failed or lost call
/// leaves the code in <c>billing.payment_links</c> and the next run sends the same one. The call asks for the invoice's amount, which
/// <see cref="Invoices.AmountAsync"/> reads from the ledger, because the link row holds no amount. The details and the
/// <c>paymentLink.created</c> audit record then commit together, and a rerun sends no second request for an invoice that has its link. An invoice
/// that cannot be linked logs a warning that names the invoice. The run goes on with the others and throws at the end, so the job queue retries
/// it. The payload is not used.
/// </summary>
internal sealed partial class PaymentLinks(NpgsqlDataSource controlPlane, IPayOs payOs, TimeProvider clock, ILogger<PaymentLinks> logger) : IJobHandler
{
    public const string JobKind = "createPaymentLinks";

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var due = new List<Guid>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT i.id FROM billing.invoices i LEFT JOIN billing.payment_links l ON l.invoice_id = i.id
            WHERE i.status = 'open' AND i.currency = 'VND' AND (l.invoice_id IS NULL OR l.created_at IS NULL)
            ORDER BY i.created_at, i.id
            """))
        {
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add(reader.GetGuid(0));
            }
        }

        var failed = 0;
        foreach (var id in due)
        {
            try
            {
                await LinkAsync(job, id, cancellationToken);
            }
            catch (Exception exception) when (exception is PayOsException or InvalidOperationException)
            {
                LogNotLinked(exception, id);
                failed++;
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"{failed} of {due.Count} invoices could not get their payment link.");
        }
    }

    private async Task LinkAsync(ClaimedJob job, Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);

        // Saved before the call, in a transaction of its own, so the code survives a call that fails or is lost.
        long orderCode;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            await using (var insert = new NpgsqlCommand(
                "INSERT INTO billing.payment_links (invoice_id) VALUES (@invoice) ON CONFLICT (invoice_id) DO NOTHING",
                connection,
                transaction))
            {
                insert.Parameters.AddWithValue("invoice", invoiceId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var select = new NpgsqlCommand("SELECT order_code FROM billing.payment_links WHERE invoice_id = @invoice", connection, transaction))
            {
                select.Parameters.AddWithValue("invoice", invoiceId);
                orderCode = (long)(await select.ExecuteScalarAsync(cancellationToken))!;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var amount = await Invoices.AmountAsync(connection, invoiceId, cancellationToken);
        var link = await payOs.CreatePaymentLinkAsync(new PayOsPaymentRequest(orderCode, amount, $"AXIS {orderCode}"), cancellationToken);

        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            int updated;
            await using (var update = new NpgsqlCommand(
                """
                UPDATE billing.payment_links
                SET payment_link_id = @paymentLinkId, checkout_url = @checkoutUrl, qr_code = @qrCode, bin = @bin,
                    account_number = @accountNumber, account_name = @accountName, description = @description, created_at = @now
                WHERE invoice_id = @invoice AND created_at IS NULL
                """,
                connection,
                transaction))
            {
                update.Parameters.AddWithValue("paymentLinkId", link.PaymentLinkId);
                update.Parameters.AddWithValue("checkoutUrl", link.CheckoutUrl);
                update.Parameters.AddWithValue("qrCode", link.QrCode);
                update.Parameters.AddWithValue("bin", link.Bin);
                update.Parameters.AddWithValue("accountNumber", link.AccountNumber);
                update.Parameters.AddWithValue("accountName", link.AccountName);
                update.Parameters.AddWithValue("description", link.Description);
                update.Parameters.AddWithValue("now", clock.GetUtcNow());
                update.Parameters.AddWithValue("invoice", invoiceId);
                updated = await update.ExecuteNonQueryAsync(cancellationToken);
            }

            if (updated == 1)
            {
                await AuditRecords.AppendAsync(transaction, AuditActorKind.Job, job.Id.ToString(), "paymentLink.created", invoiceId.ToString(), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The invoice {InvoiceId} could not get its payment link. The next run tries again.")]
    private partial void LogNotLinked(Exception exception, Guid invoiceId);
}
