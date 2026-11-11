using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Npgsql;

namespace Axis.ControlPlane.Billing;

/// <summary>
/// Runs the recurring <c>createPeriodInvoices</c> job: it creates the invoice of the next paid period of each subscription
/// <see cref="LeadTime"/> before the period starts, see <c>docs/reference/payments.md#invoices</c>. The period that it invoices starts at the
/// subscription's <see cref="Subscriptions.SubscriptionRow.PeriodEndsAt"/>, which the job reads and never writes, see
/// <c>docs/reference/subscriptions.md#one-clock</c>. A subscription that pins no plan, currency or period, or is <c>readOnly</c>,
/// <c>cancelled</c>, <c>deleting</c> or <c>deleted</c>, gets no invoice. A run after the period has started still creates its invoice, and a rerun
/// adds nothing, as <see cref="Invoices.CreatePeriodAsync"/> keeps one invoice for each period. Each new invoice and its
/// <c>invoice.created</c> audit record commit together. A subscription that cannot be invoiced logs a warning that names the subscription, never the
/// tenant. The run goes on with the others and throws at the end, so the job queue retries it. The payload is not used.
/// </summary>
internal sealed partial class PeriodInvoicing(NpgsqlDataSource controlPlane, PlanCatalogue catalogue, TimeProvider clock, ILogger<PeriodInvoicing> logger) : IJobHandler
{
    public const string JobKind = "createPeriodInvoices";

    /// <summary>How long before a period starts its invoice is created.</summary>
    public static readonly TimeSpan LeadTime = TimeSpan.FromDays(7);

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var now = clock.GetUtcNow();
        var due = new List<(Guid Id, DateTimeOffset PeriodStart)>();
        await using (var select = controlPlane.CreateCommand(
            """
            SELECT s.id, s.period_ends_at FROM control_plane.subscriptions s
            WHERE s.plan_code IS NOT NULL AND s.currency IS NOT NULL AND s.period IS NOT NULL
              AND s.period_ends_at IS NOT NULL AND s.period_ends_at <= @now + @lead
              AND s.state NOT IN ('readOnly', 'cancelled', 'deleting', 'deleted')
              AND NOT EXISTS (
                  SELECT 1 FROM billing.invoices i
                  WHERE i.subscription_id = s.id AND i.kind = 'period' AND i.period_start = s.period_ends_at)
            ORDER BY s.period_ends_at, s.id
            """))
        {
            select.Parameters.AddWithValue("now", now);
            select.Parameters.AddWithValue("lead", LeadTime);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add((reader.GetGuid(0), reader.GetFieldValue<DateTimeOffset>(1)));
            }
        }

        var failed = 0;
        foreach (var (id, periodStart) in due)
        {
            try
            {
                await InvoiceAsync(job, id, periodStart, now, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                LogNotInvoiced(exception, id);
                failed++;
            }
        }

        if (failed > 0)
        {
            throw new InvalidOperationException($"{failed} of {due.Count} subscriptions could not get their period invoice.");
        }
    }

    private async Task InvoiceAsync(ClaimedJob job, Guid id, DateTimeOffset periodStart, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var invoice = await Invoices.CreatePeriodAsync(transaction, id, periodStart, catalogue, now, cancellationToken);
        if (invoice.Created)
        {
            await AuditRecords.AppendAsync(transaction, AuditActorKind.Job, job.Id.ToString(), "invoice.created", invoice.Id.ToString(), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(LogLevel.Warning, "The subscription {SubscriptionId} could not get its period invoice. The next run tries again.")]
    private partial void LogNotInvoiced(Exception exception, Guid subscriptionId);
}
