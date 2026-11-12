using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.Subscriptions;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// A payment that makes a subscription <c>active</c>, on a real PostgreSQL against a cell that a message handler stands in for, see
/// <c>docs/reference/subscriptions.md#states</c>. A payment is recorded and applied in one transaction, as the webhook job does it. The jobs that the
/// move queues then run through the job queue: the write of the plan's quotas and, after <c>pastDue</c> or <c>readOnly</c>, the resume of the tenant.
/// Each test class instance has a container of its own, with the region <c>vn</c> and the cell <c>vn-1</c>. Every tenant holds a marker of ten digits.
/// </summary>
public sealed class SubscriptionPaymentTests : IAsyncLifetime
{
    private const string Tenant = "pay-4172903856";

    private const string OtherTenant = "pay-5938162047";

    private const long PlanVnd = 4_900_000;

    private static readonly DateTimeOffset _now = new(2026, 11, 10, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _trialEnd = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _periodEnd = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
    private readonly LogCollector _logs = new();
    private readonly CellHandler _cell = new();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private JobRunner _runner = null!;

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await _container.StartAsync(token);
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseNpgsql(new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString)
            .Options;
        await using (var context = new DirectoryDbContext(options))
        {
            await context.Database.MigrateAsync(token);
        }

        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), token);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await Execute("INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('vn', 'vn.example.com', 'https://api.vn.example.com')");
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-1', 'vn', 'https://vn-1.test', 100, 2)");
        foreach (var tenant in new[] { Tenant, OtherTenant })
        {
            await Execute($"INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('{tenant}', 'vn', 'vn-1')");
        }

        var controlPlane = new ControlPlaneOptions
        {
            TenantDomain = "vn.example.com",
            PublicUrl = "https://account.vn.example.com",
            MailFrom = "no-reply@vn.example.com",
        };
        controlPlane.Cells.Add(new CellOptions { Id = "vn-1", InternalUrl = "https://vn-1.test", TokenUrl = "https://vn-1.test/token", ClientId = "axis-controlplane" });
        controlPlane.CellSecrets["vn-1"] = "cell-secret";
        _clients = new CellClients(controlPlane, new HandlerFactory(_cell), new CellClientOptions(), TimeProvider.System);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        var quotas = new SubscriptionQuotas(_dataSource, _dataSource, _clients, _catalogue, TimeProvider.System, _loggers.CreateLogger<SubscriptionQuotas>());
        var resume = new SubscriptionResume(_dataSource, _dataSource, _clients, _loggers.CreateLogger<SubscriptionResume>());
        _runner = new JobRunner(_dataSource, [quotas, resume], new JobRunnerOptions { BaseDelay = TimeSpan.Zero }, _loggers.CreateLogger<JobRunner>());
    }

    public async ValueTask DisposeAsync()
    {
        _clients.Dispose();
        _loggers.Dispose();
        _cell.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Theory]
    [InlineData("readOnly")]
    [InlineData("pastDue")]
    public async Task A_payment_that_settles_the_invoice_of_a_read_only_or_past_due_subscription_makes_it_active_and_the_jobs_resume_the_tenant_and_write_its_quotas(string state)
    {
        var id = await SeedAsync(Tenant, state, _now.AddDays(-30), _periodEnd);
        var before = await StepAsync(id);
        var invoice = await InvoiceAsync(id, _periodEnd);

        var moved = await PayAsync(invoice, PlanVnd);

        Assert.True(moved);
        Assert.Equal("active", await StateAsync(id));
        var step = await StepAsync(id);
        Assert.NotEqual(before, step);
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("webhook", "inbox-4172903856", "subscription.active"), (record.ActorKind, record.Actor, record.Action));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}'"));
        Assert.Empty(_cell.Calls);

        await DrainAsync();

        var resume = Assert.Single(_cell.Resumes);
        Assert.Equal($"/api/cell/tenants/{Tenant}/resume", resume.Path);
        Assert.Equal($"resume:{step}", resume.Key);
        var put = Assert.Single(_cell.Puts);
        Assert.Equal($"/api/cell/tenants/{Tenant}/quotas", put.Path);
        Assert.Equal(25, put.Set[QuotaKeys.Users]);
        Assert.Equal(10_737_418_240, put.Set[QuotaKeys.Storage]);
        Assert.Equal("2026-10-01T00:00", await TextAsync($"SELECT to_char(period_ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI') FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE state <> 'succeeded'"));
    }

    [Fact]
    public async Task A_resume_that_fails_is_retried_by_the_job_queue_with_the_same_key()
    {
        var id = await SeedAsync(Tenant, "readOnly", _now.AddDays(-30), _periodEnd);
        await PayAsync(await InvoiceAsync(id, _periodEnd), PlanVnd);
        var step = await StepAsync(id);
        _cell.FailResumes = true;

        // One run for each of the two queued jobs, whichever the queue claims first.
        Assert.True(await _runner.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.True(await _runner.RunOnceAsync(TestContext.Current.CancellationToken));

        Assert.Single(_cell.Resumes);
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}' AND state <> 'succeeded'"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("could not be resumed", StringComparison.Ordinal));
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Tenant, warning.Message, StringComparison.Ordinal);

        _cell.FailResumes = false;
        await DrainAsync();

        Assert.Equal(2, _cell.Resumes.Count);
        Assert.All(_cell.Resumes, call => Assert.Equal($"resume:{step}", call.Key));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE state <> 'succeeded'"));
    }

    [Fact]
    public async Task A_resume_for_a_step_that_a_later_move_replaced_calls_nothing()
    {
        var id = await SeedAsync(Tenant, "pastDue", _now.AddDays(-30), _periodEnd);
        await PayAsync(await InvoiceAsync(id, _periodEnd), PlanVnd);
        await Execute($"UPDATE control_plane.subscriptions SET state = 'readOnly', step_id = '{Guid.CreateVersion7()}' WHERE id = '{id}'");

        await DrainAsync();

        Assert.Empty(_cell.Resumes);
        Assert.Single(_cell.Puts);
    }

    [Fact]
    public async Task A_trials_first_payment_makes_it_active_keeps_its_records_starts_the_period_at_the_trials_end_and_only_writes_the_quotas()
    {
        var id = await SeedAsync(Tenant, "trialing", _trialEnd, null);
        var signUp = await ScalarAsync<Guid>($"SELECT sign_up_id FROM control_plane.subscriptions WHERE id = '{id}'");
        var invoice = await InvoiceAsync(id, _trialEnd);

        Assert.True(await PayAsync(invoice, PlanVnd));
        await DrainAsync();

        Assert.Equal("active", await StateAsync(id));
        Assert.Equal(
            $"{Tenant}|{signUp}|2026-11-20T00:00|2026-12-20T00:00|2026-12-20T00:00",
            await TextAsync(
                $"""
                SELECT concat_ws('|', tenant_id, sign_up_id, to_char(trial_ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI'),
                    to_char(period_ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI'), to_char(next_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI'))
                FROM control_plane.subscriptions WHERE id = '{id}'
                """));
        var call = Assert.Single(_cell.Calls);
        Assert.Equal($"PUT /api/cell/tenants/{Tenant}/quotas", call);
        Assert.Empty(_cell.Resumes);
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}'"));
    }

    [Fact]
    public async Task A_trial_that_ended_unpaid_and_is_then_paid_starts_its_period_at_the_payment()
    {
        var id = await SeedAsync(Tenant, "readOnly", _now.AddDays(-5), null);

        Assert.True(await PayAsync(await InvoiceAsync(id, _now.AddDays(-5)), PlanVnd));

        Assert.Equal("2026-12-10T08:30", await TextAsync($"SELECT to_char(period_ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI') FROM control_plane.subscriptions WHERE id = '{id}'"));
    }

    [Fact]
    public async Task A_part_payment_an_overdue_open_invoice_or_an_active_subscription_changes_no_state_and_queues_nothing()
    {
        var part = await SeedAsync(Tenant, "readOnly", _now.AddDays(-30), _periodEnd);
        var overdue = await SeedAsync(OtherTenant, "pastDue", _now.AddDays(-30), _periodEnd);
        var active = await SeedAsync("pay-7305182946", "active", _now.AddDays(-30), _periodEnd);

        Assert.False(await PayAsync(await InvoiceAsync(part, _periodEnd), PlanVnd - 1_000_000));
        // The older invoice is paid, and the newer one is open and past its due date.
        var first = await InvoiceAsync(overdue, _periodEnd);
        await InvoiceAsync(overdue, _periodEnd.AddMonths(1));
        Assert.False(await PayAsync(first, PlanVnd));
        Assert.False(await PayAsync(await InvoiceAsync(active, _periodEnd), PlanVnd));

        Assert.Equal(("readOnly", "pastDue", "active"), (await StateAsync(part), await StateAsync(overdue), await StateAsync(active)));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'subscription.active'"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
    }

    private async Task DrainAsync()
    {
        while (await _runner.RunOnceAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    // The payment of a webhook event, as the job applies it: the ledger entry, the audit record and the subscription's move in one transaction.
    private async Task<bool> PayAsync(Guid invoice, long amount)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var reference = $"FT{Guid.CreateVersion7():N}";
        var tenant = await ScalarAsync<string>($"SELECT s.tenant_id FROM billing.invoices i JOIN control_plane.subscriptions s ON s.id = i.subscription_id WHERE i.id = '{invoice}'");
        Assert.True(await Payments.RecordAsync(transaction, invoice, amount, "VND", reference, $"payos:{reference}", token));
        var moved = await SubscriptionPayments.ApplyAsync(transaction, invoice, AuditActorKind.Webhook, $"inbox-{tenant["pay-".Length..]}", _now, token);
        await transaction.CommitAsync(token);
        return moved;
    }

    private async Task<Guid> InvoiceAsync(Guid subscription, DateTimeOffset periodStart)
    {
        var token = TestContext.Current.CancellationToken;
        await using var connection = await _dataSource.OpenConnectionAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var invoice = await Invoices.CreatePeriodAsync(transaction, subscription, periodStart, _catalogue, _now, token);
        await transaction.CommitAsync(token);
        return invoice.Id;
    }

    private async Task<IReadOnlyList<AuditRecord>> AuditAsync(string subject)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return await AuditRecords.ReadAsync(connection, subject, TestContext.Current.CancellationToken);
    }

    // A subscription of a verified sign-up that pins version 1 of the team plan in VND, by the month.
    private async Task<Guid> SeedAsync(string tenant, string state, DateTimeOffset trialEnd, DateTimeOffset? periodEnd)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await Execute($"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, step_id, plan_code, plan_version, currency, period, period_ends_at)
            VALUES ('{id}', '{tenant}', '{signUp}', '{state}', '{trialEnd:O}', '{Guid.CreateVersion7()}', 'team', 1, 'VND', 'month', {(periodEnd is { } end ? $"'{end:O}'" : "NULL")})
            """);
        return id;
    }

    private Task<string> StateAsync(Guid id) => ScalarAsync<string>($"SELECT state FROM control_plane.subscriptions WHERE id = '{id}'");

    private Task<Guid> StepAsync(Guid id) => ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{id}'");

    private Task<string> TextAsync(string sql) => ScalarAsync<string>(sql);

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record ChangeCall(string Path, string? Key);

    private sealed record PutCall(string Path, IReadOnlyDictionary<string, long> Set);

    /// <summary>
    /// Stands in for the cell <c>vn-1</c>: a token endpoint, the job route, <c>POST /api/cell/tenants/{id}/resume</c> and <c>PUT /api/cell/tenants/{id}/quotas</c>.
    /// It records each call with its <c>Idempotency-Key</c>. While <see cref="FailResumes"/> is set a resume answers <c>500</c> and is still recorded.
    /// </summary>
    private sealed class CellHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<ChangeCall> _resumes = new();
        private readonly ConcurrentQueue<PutCall> _puts = new();
        private readonly ConcurrentQueue<string> _calls = new();

        public bool FailResumes { get; set; }

        public IReadOnlyList<ChangeCall> Resumes => [.. _resumes];

        public IReadOnlyList<PutCall> Puts => [.. _puts];

        /// <summary>Every call that changes a tenant, as the method and the path, in the order it came.</summary>
        public IReadOnlyList<string> Calls => [.. _calls];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/token")
            {
                return Json(new { access_token = "token", expires_in = 3600, token_type = "Bearer" });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/cell/jobs/", StringComparison.Ordinal))
            {
                return Json(new { id = Guid.Parse(path["/api/cell/jobs/".Length..]), kind = "job", tenantId = "any", state = "succeeded", createdAt = DateTimeOffset.UnixEpoch });
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/resume", StringComparison.Ordinal))
            {
                _calls.Enqueue($"POST {path}");
                _resumes.Enqueue(new ChangeCall(path, request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));
                return FailResumes
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json(new { id = Guid.NewGuid(), kind = "resume", tenantId = "any", state = "queued", createdAt = DateTimeOffset.UnixEpoch }, HttpStatusCode.Accepted);
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/quotas", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var set = body.RootElement.GetProperty("set").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt64());
                _calls.Enqueue($"PUT {path}");
                _puts.Enqueue(new PutCall(path, set));
                return Json(new { quotas = set.ToDictionary(pair => pair.Key, pair => new { limit = pair.Value }) });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
    }
}
