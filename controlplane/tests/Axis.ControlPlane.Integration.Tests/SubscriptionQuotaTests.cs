using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.Admin;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.Subscriptions;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The write of a subscription's plan quotas to its cell, on a real PostgreSQL against a cell that a message handler stands in for and a clock
/// that a test moves, see <c>docs/reference/plans.md#from-a-plan-to-the-quotas</c>. Each test class instance has a container of its own, with the
/// region <c>vn</c>, the cell <c>vn-1</c> and the directory tenants of the tests. Every tenant, operator and reason holds a marker of ten digits
/// that cannot appear by chance. The plan <c>team</c> has the versions 1 and 2, each with the add-on <c>users-10</c>.
/// </summary>
public sealed class SubscriptionQuotaTests : IAsyncLifetime
{
    private const string Tenant = "quota-4172903856";

    private const string OtherTenant = "quota-5938162047";

    private const string Operator = "ops-6203918475";

    private const string Reason = "reason-8260413975";

    private const string Quotas = """
        "quotas": {
          "storage": {{storage}},
          "applications": {{applications}},
          "emailsPerDay": 2000,
          "packageCpuSecondsPerDay": 3600,
          "concurrentImports": 2,
          "concurrentConnectorCalls": 10,
          "concurrentQueries": 10,
          "workerWeight": 2,
          "authoringRequests": 200
        }
        """;

    private static readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();
    private readonly CellHandler _cell = new();
    private readonly MovedClock _clock = new(_start);
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private SubscriptionQuotas _quotas = null!;
    private QuotaSync _sync = null!;
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
        _clients = new CellClients(controlPlane, new HandlerFactory(_cell), new CellClientOptions(), _clock);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        _quotas = new SubscriptionQuotas(_dataSource, _dataSource, _clients, Catalogue(), _clock, _loggers.CreateLogger<SubscriptionQuotas>());
        _sync = new QuotaSync(_dataSource);
        _runner = new JobRunner(_dataSource, [_quotas, _sync], new JobRunnerOptions(), _loggers.CreateLogger<JobRunner>());
    }

    public async ValueTask DisposeAsync()
    {
        _clients.Dispose();
        _loggers.Dispose();
        _cell.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_subscription_that_pins_a_plan_gets_one_PUT_with_every_plan_quota_and_users_as_included_plus_add_on_users_and_a_new_version_sends_the_new_quotas()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{id}', 'users-10', 2)");

        await Write(id);

        var put = Assert.Single(_cell.Puts);
        Assert.Equal($"/api/cell/tenants/{Tenant}/quotas", put.Path);
        Assert.False(put.HasIdempotencyKey);
        Assert.Equal(QuotaKeys.All.Order(StringComparer.Ordinal), put.Set.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(25 + (2 * 10), put.Set[QuotaKeys.Users]);
        Assert.Equal(10_737_418_240, put.Set[QuotaKeys.Storage]);
        Assert.Equal(5, put.Set[QuotaKeys.Applications]);
        Assert.Equal(2000, put.Set[QuotaKeys.EmailsPerDay]);
        Assert.Equal(3600, put.Set[QuotaKeys.PackageCpuSecondsPerDay]);
        Assert.Equal(200, put.Set[QuotaKeys.AuthoringRequests]);

        await Execute($"UPDATE control_plane.subscriptions SET plan_version = 2 WHERE id = '{id}'");
        await Write(id);

        Assert.Equal(2, _cell.Puts.Count);
        var again = _cell.Puts[1];
        Assert.Equal(QuotaKeys.All.Order(StringComparer.Ordinal), again.Set.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(40 + (2 * 10), again.Set[QuotaKeys.Users]);
        Assert.Equal(21_474_836_480, again.Set[QuotaKeys.Storage]);
        Assert.Equal(10, again.Set[QuotaKeys.Applications]);
        Assert.Equal(3600, again.Set[QuotaKeys.PackageCpuSecondsPerDay]);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records"));
    }

    [Fact]
    public async Task A_quota_changed_on_the_cell_is_put_back_by_the_sync_and_the_jobs_it_queues_and_a_deleting_or_unpinned_subscription_gets_none()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);
        await SeedAsync(OtherTenant, "deleting", pinned: true);
        await SeedAsync("quota-7305182946", "trialing", pinned: false);
        await Write(id);
        _cell.Quotas[Tenant][QuotaKeys.Storage] = 1;
        _cell.Quotas[Tenant][QuotaKeys.Users] = 3;
        var before = _cell.Puts.Count;

        await _sync.RunAsync(new ClaimedJob(Guid.NewGuid(), QuotaSync.JobKind, [], 1), TestContext.Current.CancellationToken);

        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        await DrainAsync();
        Assert.Equal(before + 1, _cell.Puts.Count);
        Assert.Equal(10_737_418_240, _cell.Quotas[Tenant][QuotaKeys.Storage]);
        Assert.Equal(25, _cell.Quotas[Tenant][QuotaKeys.Users]);
        Assert.DoesNotContain(_cell.Puts, put => put.Path.Contains(OtherTenant, StringComparison.Ordinal));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE state <> 'succeeded'"));
    }

    [Fact]
    public async Task An_override_is_sent_instead_of_the_plan_value_until_its_end_date_and_the_sync_sends_the_plan_value_after_it()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);
        await Write(id);
        var until = new DateTimeOffset(_start.AddDays(10).UtcDateTime.Date, TimeSpan.Zero);

        var run = await OverrideAsync("users", 100, until);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal($"{id}  users 100 until {until:yyyy-MM-ddTHH:mm:ssZ}  job {await ScalarAsync<Guid>("SELECT id FROM control_plane.jobs")}", run.Output.Trim());
        await DrainAsync();
        Assert.Equal(100, _cell.Quotas[Tenant][QuotaKeys.Users]);
        Assert.Equal(10_737_418_240, _cell.Quotas[Tenant][QuotaKeys.Storage]);

        // The end date is not reached on day 9, so the nightly sync keeps the override.
        _clock.Now = _start.AddDays(9);
        await SyncAsync();
        Assert.Equal(100, _cell.Quotas[Tenant][QuotaKeys.Users]);

        _clock.Now = until.AddMinutes(1);
        await SyncAsync();
        Assert.Equal(25, _cell.Quotas[Tenant][QuotaKeys.Users]);
        Assert.Equal(25, _cell.Puts[^1].Set[QuotaKeys.Users]);
    }

    [Fact]
    public async Task A_new_override_replaces_the_old_one_for_the_same_quota_and_keeps_both_audit_records()
    {
        await SeedAsync(Tenant, "trialing", pinned: true);
        var until = new DateTimeOffset(_start.AddDays(10).UtcDateTime.Date, TimeSpan.Zero);

        Assert.Equal(0, (await OverrideAsync("storage", 100, until)).ExitCode);
        Assert.Equal(0, (await OverrideAsync("storage", 200, until.AddDays(1))).ExitCode);

        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.quota_overrides"));
        Assert.Equal(200L, await ScalarAsync<long>("SELECT value FROM control_plane.quota_overrides"));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'quota.overridden'"));
        await DrainAsync();
        Assert.Equal(200, _cell.Quotas[Tenant][QuotaKeys.Storage]);
    }

    [Fact]
    public async Task An_override_through_the_admin_command_stores_one_override_writes_one_operator_audit_record_queues_one_job_and_prints_no_reason()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);

        var run = await RunAdminAsync(Tenant, "users", "100", Date(10), Reason);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("", run.Error);
        Assert.DoesNotContain(Reason, run.Output, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.quota_overrides WHERE subscription_id = '{id}' AND quota = 'users' AND value = 100 AND operator = '{Operator}' AND reason = '{Reason}'"));
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("operator", Operator, "quota.overridden"), (record.ActorKind, record.Actor, record.Action));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}' AND state = 'queued'"));
    }

    [Theory]
    [InlineData("noReason")]
    [InlineData("blankReason")]
    [InlineData("noUntil")]
    [InlineData("badDate")]
    [InlineData("unknownQuota")]
    [InlineData("badValue")]
    [InlineData("negativeValue")]
    public async Task An_override_with_no_or_a_blank_reason_or_bad_arguments_exits_2_and_stores_no_override_audit_record_or_job(string case_)
    {
        await SeedAsync(Tenant, "trialing", pinned: true);
        string[] args = case_ switch
        {
            "noReason" => ["quota", "override", Tenant, "users", "100", "--until", Date(10), "--operator", Operator],
            "blankReason" => ["quota", "override", Tenant, "users", "100", "--until", Date(10), "--reason", "   ", "--operator", Operator],
            "noUntil" => ["quota", "override", Tenant, "users", "100", "--reason", Reason, "--operator", Operator],
            "badDate" => ["quota", "override", Tenant, "users", "100", "--until", "10/12/2030", "--reason", Reason, "--operator", Operator],
            "unknownQuota" => ["quota", "override", Tenant, "seats", "100", "--until", Date(10), "--reason", Reason, "--operator", Operator],
            "badValue" => ["quota", "override", Tenant, "users", "1.5", "--until", Date(10), "--reason", Reason, "--operator", Operator],
            _ => ["quota", "override", Tenant, "users", "-1", "--until", Date(10), "--reason", Reason, "--operator", Operator],
        };

        var run = await RunAsync(args);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.StartsWith("Usage:", run.Error, StringComparison.Ordinal);
        Assert.Contains("quota override", run.Error, StringComparison.Ordinal);
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData("past")]
    [InlineData("unpinned")]
    [InlineData("deleting")]
    [InlineData("noSubscription")]
    public async Task An_override_for_a_past_end_date_or_a_tenant_without_a_pinned_plan_is_refused_with_exit_1_and_stores_nothing(string case_)
    {
        await SeedAsync(Tenant, case_ == "deleting" ? "deleting" : "trialing", pinned: case_ is "past" or "deleting");
        var tenant = case_ == "noSubscription" ? "quota-9104857362" : Tenant;
        var until = case_ == "past" ? DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : Date(10);

        var run = await RunAdminAsync(tenant, "users", "100", until, Reason);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("", run.Output);
        Assert.DoesNotContain(Reason, run.Error, StringComparison.Ordinal);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task A_subscription_that_is_deleted_or_pins_no_plan_gets_no_PUT_and_the_job_succeeds()
    {
        var unpinned = await SeedAsync(Tenant, "trialing", pinned: false);
        var deleted = await SeedAsync(OtherTenant, "deleted", pinned: true);

        await Write(unpinned);
        await Write(deleted);
        await Write(Guid.NewGuid());

        Assert.Empty(_cell.Puts);
    }

    [Fact]
    public async Task An_add_on_that_the_pinned_plan_does_not_list_fails_the_job_with_the_subscription_and_the_code_but_not_the_tenant_and_sends_nothing()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{id}', 'storage-pack', 1)");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Write(id));

        Assert.Contains(id.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("storage-pack", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Tenant, exception.Message, StringComparison.Ordinal);
        Assert.Empty(_cell.Puts);
    }

    [Fact]
    public async Task A_cell_that_refuses_the_PUT_fails_the_job_with_a_warning_that_names_the_subscription_and_the_cell_and_a_later_run_writes_the_same_rows()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);
        _cell.FailPuts = true;

        var exception = await Assert.ThrowsAsync<CellCallException>(() => Write(id));

        Assert.Equal(500, exception.Status);
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Tenant, warning.Message, StringComparison.Ordinal);

        _cell.FailPuts = false;
        await Write(id);
        await Write(id);

        Assert.Equal(3, _cell.Puts.Count);
        Assert.Equal(_cell.Puts[1].Set, _cell.Puts[2].Set);
        Assert.Equal(25, _cell.Quotas[Tenant][QuotaKeys.Users]);
    }

    [Fact]
    public async Task A_tenant_with_no_directory_row_fails_the_job_with_a_warning_that_names_the_subscription_and_not_the_tenant()
    {
        var id = await SeedAsync("quota-3849201756", "trialing", pinned: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Write(id));

        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("quota-3849201756", warning.Message, StringComparison.Ordinal);
        Assert.Empty(_cell.Puts);
    }

    [Fact]
    public async Task The_database_refuses_a_blank_reason_a_negative_value_and_an_add_on_quantity_below_1()
    {
        var id = await SeedAsync(Tenant, "trialing", pinned: true);

        var blank = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            $"INSERT INTO control_plane.quota_overrides (subscription_id, quota, value, reason, until, operator) VALUES ('{id}', 'users', 1, '  ', now(), '{Operator}')"));
        var negative = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            $"INSERT INTO control_plane.quota_overrides (subscription_id, quota, value, reason, until, operator) VALUES ('{id}', 'users', -1, '{Reason}', now(), '{Operator}')"));
        var quantity = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            $"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{id}', 'users-10', 0)"));

        Assert.All([blank, negative, quantity], exception => Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState));
    }

    private static string Date(int days) => _start.AddDays(days).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task Write(Guid subscriptionId) =>
        _quotas.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionQuotas.JobKind, new() { ["subscriptionId"] = subscriptionId.ToString() }, 1), TestContext.Current.CancellationToken);

    // The nightly job and the writes that it queues.
    private async Task SyncAsync()
    {
        await _sync.RunAsync(new ClaimedJob(Guid.NewGuid(), QuotaSync.JobKind, [], 1), TestContext.Current.CancellationToken);
        await DrainAsync();
    }

    private async Task DrainAsync()
    {
        while (await _runner.RunOnceAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    private async Task AssertNothingStoredAsync()
    {
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.quota_overrides"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records WHERE action = 'quota.overridden'"));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
    }

    private async Task<(int ExitCode, string Output, string Error)> OverrideAsync(string quota, long value, DateTimeOffset until)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var options = new ControlPlaneOptions { ConnectionString = _container.GetConnectionString() };
        var exitCode = await OperatorCommands.QuotaOverrideAsync(
            options, Tenant, quota, value, until, Reason, Operator, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private Task<(int ExitCode, string Output, string Error)> RunAdminAsync(string tenant, string quota, string value, string until, string reason) =>
        RunAsync(["quota", "override", tenant, quota, value, "--until", until, "--reason", reason, "--operator", Operator]);

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing",
            ["ControlPlane:Region"] = "vn",
            ["ControlPlane:TenantDomain"] = "vn.example.com",
            ["ControlPlane:ConnectionString"] = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Pooling = false }.ConnectionString,
            ["ControlPlane:MailFrom"] = "no-reply@vn.example.com",
            ["ControlPlane:Mail:Host"] = "mail.invalid",
        }).Build();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ControlPlaneAdminCommands.RunAsync(args, configuration, output, error, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private async Task<IReadOnlyList<AuditRecord>> AuditAsync(string subject)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return await AuditRecords.ReadAsync(connection, subject, TestContext.Current.CancellationToken);
    }

    // A subscription of a verified sign-up. A pinned one has version 1 of the team plan.
    private async Task<Guid> SeedAsync(string tenant, string state, bool pinned)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await Execute($"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, step_id{(pinned ? ", plan_code, plan_version, currency, period" : "")})
            VALUES ('{id}', '{tenant}', '{signUp}', '{state}', now(), '{Guid.CreateVersion7()}'{(pinned ? ", 'team', 1, 'VND', 'month'" : "")})
            """);
        return id;
    }

    // The team plan in the versions 1 and 2. Version 2 includes more users and has higher quotas.
    private static PlanCatalogue Catalogue()
    {
        var json = $$"""{ "ControlPlane": { "plans": [{{Plan(2, 40, 21_474_836_480, 10)}},{{Plan(1, 25, 10_737_418_240, 5)}}] } }""";
        var configuration = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }

    private static string Plan(int version, int includedUsers, long storage, int applications) => $$"""
        {
          "code": "team",
          "version": {{version}},
          "name": { "en": "Team", "vi": "Nhóm" },
          "includedUsers": {{includedUsers}},
          {{Quotas.Replace("{{storage}}", storage.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{{applications}}", applications.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)}},
          "prices": [{ "currency": "VND", "month": 4900000 }, { "currency": "USD", "month": 1900 }],
          "addOns": [{ "code": "users-10", "users": 10, "prices": [{ "currency": "VND", "month": 500000 }, { "currency": "USD", "month": 20 }] }]
        }
        """;

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private sealed class MovedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record PutCall(string Path, bool HasIdempotencyKey, IReadOnlyDictionary<string, long> Set);

    /// <summary>
    /// Stands in for the cell <c>vn-1</c>: a token endpoint and <c>PUT /api/cell/tenants/{id}/quotas</c>. It records each call, applies the
    /// <c>set</c> of the body to <see cref="Quotas"/> of the tenant, and answers <c>200</c> with the tenant's quotas.
    /// </summary>
    private sealed class CellHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<PutCall> _puts = new();

        /// <summary>The quotas that the tests set on the cell for each tenant, by key. A key the cell has not been given has no entry.</summary>
        public ConcurrentDictionary<string, ConcurrentDictionary<string, long>> Quotas { get; } = new() { };

        /// <summary>When set, a <c>PUT</c> answers <c>500</c> and is still recorded.</summary>
        public bool FailPuts { get; set; }

        public IReadOnlyList<PutCall> Puts => [.. _puts];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/token")
            {
                return Json(new { access_token = "token", expires_in = 3600, token_type = "Bearer" });
            }

            if (request.Method == HttpMethod.Put && path.StartsWith("/api/cell/tenants/", StringComparison.Ordinal) && path.EndsWith("/quotas", StringComparison.Ordinal))
            {
                var tenant = path["/api/cell/tenants/".Length..^"/quotas".Length];
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var set = body.RootElement.GetProperty("set").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt64());
                _puts.Enqueue(new PutCall(path, request.Headers.Contains("Idempotency-Key"), set));
                if (FailPuts)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                var quotas = Quotas.GetOrAdd(tenant, _ => new ConcurrentDictionary<string, long>());
                foreach (var (key, value) in set)
                {
                    quotas[key] = value;
                }

                return Json(new { quotas = quotas.ToDictionary(pair => pair.Key, pair => new { limit = pair.Value }) });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
    }
}
