using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The clock of the subscriptions on a real PostgreSQL, against a cell that a message handler stands in for and a clock that a test
/// moves, see <c>docs/reference/subscriptions.md#one-clock</c>. Each test class instance has a container of its own, with the region
/// <c>vn</c>, the cell <c>vn-1</c> and the directory tenants <c>acme</c>, <c>beta</c> and <c>gamma</c>. The emails of the timeline are
/// sent through the shared Mailpit, to addresses of each test's own, see <c>docs/reference/subscriptions.md#emails</c>.
/// </summary>
public sealed class SubscriptionClockTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private static readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly LogCollector _logs = new();
    private readonly CellHandler _cell = new();
    private readonly MovedClock _clock = new(_start);
    private readonly EmailTemplates _templates = EmailTemplates.LoadEmbedded();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private SubscriptionClock _subscriptions = null!;

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
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-1', 'vn', 'https://vn-1.test', 100, 3)");
        foreach (var tenant in new[] { "acme", "beta", "gamma" })
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
        _clients = new CellClients(controlPlane, new HandlerFactory(_cell), new CellClientOptions { PollDelay = TimeSpan.FromMilliseconds(10) }, _clock);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(_logs));
        _subscriptions = new SubscriptionClock(_dataSource, _dataSource, _clients, _templates, controlPlane, _clock, _loggers.CreateLogger<SubscriptionClock>());
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
    public async Task A_trial_is_readOnly_on_day_30_then_deleting_30_days_later_then_deleted_once_the_cell_answers_404_with_one_call_for_each_step()
    {
        var id = await SeedAsync("acme", "trialing", _start.AddDays(30));

        await RunAtAsync(29);
        Assert.Equal("trialing", await StateAsync(id));
        Assert.Empty(_cell.Calls);

        await RunAtAsync(30);
        Assert.Equal("readOnly", await StateAsync(id));
        Assert.Equal(_start.AddDays(60), await NextAtAsync(id));
        Assert.Equal(["POST /api/cell/tenants/acme/read-only"], _cell.Calls.Select(call => call.Call));

        await RunAtAsync(59);
        Assert.Equal("readOnly", await StateAsync(id));
        Assert.Single(_cell.Calls);

        await RunAtAsync(60);
        Assert.Equal("deleting", await StateAsync(id));
        Assert.Equal(_start.AddDays(60).AddHours(1), await NextAtAsync(id));
        Assert.Equal(["POST /api/cell/tenants/acme/read-only", "DELETE /api/cell/tenants/acme"], _cell.Calls.Select(call => call.Call));

        // The cell's grace is not over on day 61, so the subscription waits.
        _cell.Statuses["acme"] = "deleting";
        await RunAtAsync(61);
        Assert.Equal("deleting", await StateAsync(id));
        Assert.Equal(_start.AddDays(61).AddHours(1), await NextAtAsync(id));

        // The cell purged the tenant by day 67.
        _cell.Statuses.Remove("acme", out _);
        await RunAtAsync(67);
        Assert.Equal("deleted", await StateAsync(id));
        Assert.Null(await NextAtAsync(id));
        Assert.Equal(2, _cell.Calls.Count);

        await RunAtAsync(90);
        Assert.Equal("deleted", await StateAsync(id));
        Assert.Equal(2, _cell.Calls.Count);
        Assert.Equal(
            ["subscription.readOnly", "subscription.deleting", "subscription.deleted"],
            await ColumnAsync($"SELECT action FROM control_plane.audit_records WHERE subject = '{id}' ORDER BY occurred_at, id"));
    }

    [Fact]
    public async Task One_run_after_an_outage_of_two_days_makes_every_overdue_move_oldest_first_and_none_moves_two_steps()
    {
        var first = await SeedAsync("acme", "trialing", _start.AddHours(-47));
        var second = await SeedAsync("beta", "readOnly", _start.AddHours(-30));
        var third = await SeedAsync("gamma", "trialing", _start.AddDays(-1));
        _clock.Now = _start;

        await Run();

        Assert.Equal(
            ["POST /api/cell/tenants/acme/read-only", "DELETE /api/cell/tenants/beta", "POST /api/cell/tenants/gamma/read-only"],
            _cell.Calls.Select(call => call.Call));
        Assert.Equal("readOnly", await StateAsync(first));
        Assert.Equal("deleting", await StateAsync(second));
        Assert.Equal("readOnly", await StateAsync(third));

        // The 30 days count from the run, so a late run never shortens the wait.
        Assert.Equal(_start.AddDays(30), await NextAtAsync(first));
        Assert.Equal(_start.AddHours(1), await NextAtAsync(second));
    }

    [Fact]
    public async Task Each_move_writes_one_audit_record_with_the_job_as_actor_and_a_second_run_at_the_same_time_writes_none()
    {
        var id = await SeedAsync("acme", "trialing", _start.AddDays(-1));
        var job = new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1);

        await _subscriptions.RunAsync(job, TestContext.Current.CancellationToken);
        await _subscriptions.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), TestContext.Current.CancellationToken);

        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records"));
        Assert.Equal("job", await ScalarAsync<string>($"SELECT actor_kind FROM control_plane.audit_records WHERE subject = '{id}'"));
        Assert.Equal(job.Id.ToString(), await ScalarAsync<string>($"SELECT actor FROM control_plane.audit_records WHERE subject = '{id}'"));
        Assert.Equal("subscription.readOnly", await ScalarAsync<string>($"SELECT action FROM control_plane.audit_records WHERE subject = '{id}'"));
        Assert.Single(_cell.Calls);
    }

    [Fact]
    public async Task A_cell_call_that_fails_leaves_the_subscription_unchanged_and_the_run_throws_and_the_next_run_sends_the_same_key_and_makes_the_move()
    {
        var id = await SeedAsync("acme", "trialing", _start.AddDays(-1));
        var stepId = await ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{id}'");
        _cell.FailChanges = true;

        await Assert.ThrowsAsync<InvalidOperationException>(Run);

        Assert.Equal("trialing", await StateAsync(id));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("acme", warning.Message, StringComparison.Ordinal);

        _cell.FailChanges = false;
        await Run();

        Assert.Equal("readOnly", await StateAsync(id));
        Assert.Equal(2, _cell.Calls.Count);
        Assert.Equal($"read-only:{stepId:D}", _cell.Calls[0].Key);
        Assert.Equal(_cell.Calls[0].Key, _cell.Calls[1].Key);
        Assert.NotEqual(stepId, await ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records"));
    }

    [Fact]
    public async Task A_cancelled_deletion_resumes_the_tenant_then_makes_it_read_only_and_the_subscription_goes_back_to_readOnly_for_30_days()
    {
        var id = await SeedAsync("acme", "deleting", _start.AddMinutes(-5));
        _cell.Statuses["acme"] = "suspended";

        await Run();

        Assert.Equal("readOnly", await StateAsync(id));
        Assert.Equal(_start.AddDays(30), await NextAtAsync(id));
        Assert.Equal(["POST /api/cell/tenants/acme/resume", "POST /api/cell/tenants/acme/read-only"], _cell.Calls.Select(call => call.Call));
        Assert.Equal(["subscription.readOnly"], await ColumnAsync($"SELECT action FROM control_plane.audit_records WHERE subject = '{id}'"));
    }

    [Fact]
    public async Task A_deleting_subscription_whose_directory_row_was_dropped_after_the_purge_becomes_deleted_without_a_cell_call()
    {
        var id = await SeedAsync("acme", "deleting", _start.AddMinutes(-5));
        await Execute("DELETE FROM directory.tenants WHERE id = 'acme'");

        await Run();

        Assert.Equal("deleted", await StateAsync(id));
        Assert.Null(await NextAtAsync(id));
        Assert.Empty(_cell.Calls);
        Assert.Equal(["subscription.deleted"], await ColumnAsync($"SELECT action FROM control_plane.audit_records WHERE subject = '{id}'"));
        await Run();
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.audit_records"));
    }

    [Fact]
    public async Task A_tenant_with_no_directory_row_is_left_as_it_is_with_a_warning_and_the_other_subscriptions_still_move()
    {
        var orphan = await SeedAsync("orphan", "trialing", _start.AddDays(-2));
        var moved = await SeedAsync("acme", "trialing", _start.AddDays(-1));

        await Assert.ThrowsAsync<InvalidOperationException>(Run);

        Assert.Equal("trialing", await StateAsync(orphan));
        Assert.Equal("readOnly", await StateAsync(moved));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(orphan.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("orphan", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trial_sends_each_email_of_the_timeline_once_on_its_day_to_each_administrator_and_the_owner_each_in_their_language()
    {
        var people = SeedPeople();
        var id = await SeedAsync("acme", "trialing", _start.AddDays(30), people.Owner);
        _cell.Administrators["acme"] =
        [
            Administrator(people.Vi, "Binh", "vi"),
            Administrator(people.OwnerTwin, "An", "en"),
        ];
        var runner = await NewMailRunnerAsync();
        var owner = new List<string>();
        var vi = new List<string>();

        // The cell's 7-day grace ends by day 67, and day 61 is a check that finds the tenant still deleting.
        (int Day, string? Kind)[] days =
        [
            (0, "trialStarted"), (22, null), (23, "trialEndsIn7Days"), (29, "trialEndsTomorrow"), (30, "tenantReadOnly"),
            (53, "deletionIn7Days"), (60, null), (61, null), (67, "tenantDeleted"),
        ];
        foreach (var (day, kind) in days)
        {
            if (day == 61)
            {
                _cell.Statuses["acme"] = "deleting";
            }

            if (day == 67)
            {
                _cell.Statuses.Remove("acme", out _);
            }

            await RunAtAsync(day);
            await DrainAsync(runner);
            if (kind is not null)
            {
                owner.Add(Subject(kind, "en"));
                if (kind != "tenantDeleted")
                {
                    vi.Add(Subject(kind, "vi"));
                }
            }

            Assert.Equal(owner, (await MessagesToAsync(people)).Where(message => HasRecipient(message, people.Owner)).Select(message => message.Subject));
            Assert.Equal(vi, (await MessagesToAsync(people)).Where(message => HasRecipient(message, people.Vi)).Select(message => message.Subject));
        }

        Assert.Equal("deleted", await StateAsync(id));
        Assert.Equal(6, owner.Count);
        Assert.Equal(5, vi.Count);

        // The owner and the administrator with the same address in another case got one email each, and none went to the twin's address.
        Assert.DoesNotContain((await MessagesToAsync(people)).SelectMany(message => message.To), address => address == people.OwnerTwin);
    }

    [Fact]
    public async Task An_email_names_the_date_of_the_next_step_in_utc_in_the_language_of_its_reader_and_links_only_to_the_billing_portal()
    {
        var people = SeedPeople();
        await SeedAsync("acme", "trialing", _start.AddDays(30), people.Owner);
        _cell.Administrators["acme"] = [Administrator(people.Vi, "Binh", "vi"), Administrator(people.Other, null, "fr")];
        var runner = await NewMailRunnerAsync();

        await RunAtAsync(0);
        await DrainAsync(runner);

        var messages = await MessagesToAsync(people);
        var english = Assert.Single(messages, message => HasRecipient(message, people.Owner));
        Assert.Contains("31 January 2026, 00:00 UTC", english.Text, StringComparison.Ordinal);
        Assert.Contains("https://acme.vn.example.com", english.Text, StringComparison.Ordinal);
        Assert.Contains("https://account.vn.example.com/tenants/acme", english.Text, StringComparison.Ordinal);
        var vietnamese = Assert.Single(messages, message => HasRecipient(message, people.Vi));
        Assert.Contains("00:00 UTC ngày 31/01/2026", vietnamese.Text, StringComparison.Ordinal);
        Assert.Contains("Chào Binh,", vietnamese.Text, StringComparison.Ordinal);

        // An administrator with no language of the two, and no name, gets the sign-up's language and the address as the name.
        var other = Assert.Single(messages, message => HasRecipient(message, people.Other));
        Assert.Equal(Subject("trialStarted", "en"), other.Subject);
        Assert.Contains($"Hello {people.Other},", other.Text, StringComparison.Ordinal);
        Assert.All(messages, message => Assert.DoesNotContain("http://", message.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_rerun_of_the_clock_at_each_day_queues_no_second_email_job_and_sends_no_second_email()
    {
        var people = SeedPeople();
        await SeedAsync("acme", "trialing", _start.AddDays(30), people.Owner);
        _cell.Administrators["acme"] = [Administrator(people.Vi, "Binh", "vi"), Administrator(people.OwnerTwin, "An", "en")];
        var runner = await NewMailRunnerAsync();

        foreach (var day in new[] { 0, 23, 29, 30, 53, 60 })
        {
            await RunAtAsync(day);
            var queued = await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'");
            await RunAtAsync(day);
            Assert.Equal(queued, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        }

        _cell.Statuses["acme"] = "deleting";
        await RunAtAsync(61);
        _cell.Statuses.Remove("acme", out _);
        await RunAtAsync(67);
        await RunAtAsync(67);

        // Five emails each to the owner, who is also an administrator, and to the other administrator, and the deletion's to the owner only.
        Assert.Equal(11L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal(6L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.subscription_emails"));
        await DrainAsync(runner);
        var messages = await MessagesToAsync(people);
        Assert.Equal(11, messages.Count);
        await RunAtAsync(90);
        await DrainAsync(runner);
        Assert.Equal(11, (await MessagesToAsync(people)).Count);
    }

    [Fact]
    public async Task An_administrators_read_that_fails_queues_no_email_and_the_run_throws_and_the_next_run_queues_the_emails_once()
    {
        var people = SeedPeople();
        var id = await SeedAsync("acme", "trialing", _start.AddDays(30), people.Owner);
        _cell.Administrators["acme"] = [Administrator(people.Vi, "Binh", "vi")];
        _cell.FailAdministrators = true;

        await Assert.ThrowsAsync<InvalidOperationException>(Run);

        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.subscription_emails"));
        var warning = Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.Contains(id.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Contains("vn-1", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("acme", warning.Message, StringComparison.Ordinal);

        _cell.FailAdministrators = false;
        await Run();
        await Run();

        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal(["trialStarted"], await ColumnAsync($"SELECT kind FROM control_plane.subscription_emails WHERE subscription_id = '{id}'"));
    }

    [Fact]
    public async Task A_tenant_the_cell_does_not_know_yet_gets_no_email_and_no_error_and_a_run_after_the_window_closed_skips_the_email()
    {
        var people = SeedPeople();
        var id = await SeedAsync("acme", "trialing", _start.AddDays(30), people.Owner);

        // The cell answers 404 for the administrators of a tenant that is not created yet.
        await RunAtAsync(0);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));

        // The tenant exists on day 25, after the window of the started email has closed.
        _cell.Administrators["acme"] = [Administrator(people.Vi, "Binh", "vi")];
        await RunAtAsync(25);

        Assert.Equal(["trialEndsIn7Days"], await ColumnAsync($"SELECT kind FROM control_plane.subscription_emails WHERE subscription_id = '{id}'"));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
    }

    private sealed record People(string Owner, string OwnerTwin, string Vi, string Other, string Part);

    // Mailpit is shared, so each test has addresses of its own. The twin differs from the owner's address only by a capital.
    private static People SeedPeople()
    {
        var part = Guid.NewGuid().ToString("N");
        return new People($"owner-{part}@example.org", $"Owner-{part}@example.org", $"vi-{part}@example.org", $"other-{part}@example.org", part);
    }

    private static object Administrator(string email, string? displayName, string? language) => new { email, displayName, language };

    private static bool HasRecipient(MailpitMessage message, string address) => message.To.Any(to => string.Equals(to, address, StringComparison.OrdinalIgnoreCase));

    private Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(People people) => mailpit.MessagesToAsync(people.Part, TestContext.Current.CancellationToken);

    // The subject of an email: the texts of a subject name no value.
    private string Subject(string kind, string language) =>
        _templates.Render(kind, language, _templates.Placeholders(kind).ToDictionary(name => name, name => name)).Subject;

    private async Task<JobRunner> NewMailRunnerAsync()
    {
        var options = new ControlPlaneOptions
        {
            MailFrom = "no-reply@vn.example.com",
            Mail = new ControlPlaneMailOptions { Host = await mailpit.SmtpHostAsync(), Port = await mailpit.SmtpPortAsync(), UseTls = false },
        };
        return new JobRunner(_dataSource, [new SendEmailJobHandler(options, _templates)], new JobRunnerOptions(), new LoggerFactory().CreateLogger<JobRunner>());
    }

    private static async Task DrainAsync(JobRunner runner)
    {
        while (await runner.RunOnceAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    private Task Run() => _subscriptions.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), TestContext.Current.CancellationToken);

    private Task RunAtAsync(int day)
    {
        _clock.Now = _start.AddDays(day);
        return Run();
    }

    // A subscription of a verified sign-up that is due at nextAt. The clock of the test starts at the same time as the trial.
    private async Task<Guid> SeedAsync(string tenant, string state, DateTimeOffset nextAt, string? owner = null)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{owner ?? $"{tenant}@example.com"}', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, next_at, step_id)
            VALUES (@id, @tenant, @signUp, @state, @nextAt, @nextAt, @step)
            """);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("tenant", tenant);
        insert.Parameters.AddWithValue("signUp", signUp);
        insert.Parameters.AddWithValue("state", state);
        insert.Parameters.AddWithValue("nextAt", nextAt);
        insert.Parameters.AddWithValue("step", Guid.CreateVersion7());
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private Task<string> StateAsync(Guid id) => ScalarAsync<string>($"SELECT state FROM control_plane.subscriptions WHERE id = '{id}'");

    private async Task<DateTimeOffset?> NextAtAsync(Guid id)
    {
        await using var command = _dataSource.CreateCommand($"SELECT next_at FROM control_plane.subscriptions WHERE id = '{id}'");
        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) is DateTime value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private async Task<string[]> ColumnAsync(string sql)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private sealed class MovedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record ChangeCall(string Call, string? Key);

    /// <summary>
    /// Stands in for the cell <c>vn-1</c>: a token endpoint, the routes that change a tenant, which answer a queued job, the job read,
    /// which answers <c>succeeded</c>, the administrators read and the tenant read. A tenant with no entry in <see cref="Statuses"/> answers <c>404</c>.
    /// </summary>
    private sealed class CellHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<ChangeCall> _calls = new();

        public ConcurrentDictionary<string, string> Statuses { get; } = new();

        /// <summary>The <c>items</c> that the administrators route answers for a tenant. A tenant with no entry answers <c>404</c>.</summary>
        public ConcurrentDictionary<string, object[]> Administrators { get; } = new();

        /// <summary>When set, the administrators route answers <c>500</c>.</summary>
        public bool FailAdministrators { get; set; }

        /// <summary>When set, a call that changes a tenant answers <c>500</c> and is still recorded.</summary>
        public bool FailChanges { get; set; }

        public IReadOnlyList<ChangeCall> Calls => [.. _calls];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Answer(request));

        private HttpResponseMessage Answer(HttpRequestMessage request)
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

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/cell/tenants/", StringComparison.Ordinal) && path.EndsWith("/administrators", StringComparison.Ordinal))
            {
                var tenant = path["/api/cell/tenants/".Length..^"/administrators".Length];
                if (FailAdministrators)
                {
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError);
                }

                return Administrators.TryGetValue(tenant, out var items) ? Json(new { items }) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/cell/tenants/", StringComparison.Ordinal))
            {
                var id = path["/api/cell/tenants/".Length..];
                return Statuses.TryGetValue(id, out var status)
                    ? Json(new { id, status, application = (string?)null, hosts = Array.Empty<string>() })
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method != HttpMethod.Get && path.StartsWith("/api/cell/tenants/", StringComparison.Ordinal))
            {
                _calls.Enqueue(new ChangeCall($"{request.Method.Method} {path}", request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));
                return FailChanges
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json(new { id = Guid.NewGuid(), kind = "change", tenantId = "any", state = "queued", createdAt = DateTimeOffset.UnixEpoch }, HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
    }
}
