using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Dns;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.SignUp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The <c>provisionTenant</c> job on a real PostgreSQL, against cells that a message handler stands in for and the in-memory DNS,
/// see <c>docs/reference/sign-up.md#provisioning</c>. Each test class instance has a container of its own, with the region <c>vn</c>, the
/// cells <c>vn-1</c> (10 of 100 tenants) and <c>vn-2</c> (20 of 100), and the verified sign-up of the subdomain <c>acme</c>.
/// </summary>
public sealed class ProvisionTenantJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly CellHandler _cells = new();
    private readonly InMemoryDnsRecords _dns = new();
    private readonly Guid _signUpId = Guid.CreateVersion7();
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private ProvisionTenantJobHandler _handler = null!;

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
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-1', 'vn', 'https://vn-1.test', 100, 10), ('vn-2', 'vn', 'https://vn-2.test', 100, 20)");
        await SeedSignUpAsync();

        var controlPlane = new ControlPlaneOptions
        {
            Region = "vn",
            TenantDomain = "vn.example.com",
            PublicUrl = "https://account.vn.example.com",
            MailFrom = "no-reply@vn.example.com",
        };
        controlPlane.SignUp.TrialQuotas = new Dictionary<string, long?> { ["users"] = 5, ["storage"] = null };
        foreach (var id in new[] { "vn-1", "vn-2" })
        {
            controlPlane.Cells.Add(new CellOptions { Id = id, InternalUrl = $"https://{id}.test", TokenUrl = $"https://{id}.test/token", ClientId = "axis-controlplane" });
            controlPlane.CellSecrets[id] = "cell-secret";
        }

        _clients = new CellClients(controlPlane, new HandlerFactory(_cells), new CellClientOptions { PollDelay = TimeSpan.FromMilliseconds(10) }, TimeProvider.System);
        _loggers = LoggerFactory.Create(builder => builder.AddProvider(new LogCollector()));
        _handler = new ProvisionTenantJobHandler(
            _dataSource,
            _dataSource,
            new CellPicker(_dataSource, _dataSource, _loggers.CreateLogger<CellPicker>()),
            _clients,
            EmailTemplates.LoadEmbedded(),
            controlPlane,
            TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        _clients.Dispose();
        _loggers.Dispose();
        _cells.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_confirmed_sign_up_is_created_in_the_least_full_cell_with_the_trials_quotas_and_the_directory_rows_and_the_cname_are_written()
    {
        await RunAsync();

        var post = Assert.Single(_cells.Posts);
        Assert.Equal("vn-1.test", post.Host);
        Assert.Equal($"tenant-create:{_signUpId:D}", post.Key);
        Assert.Equal("""{"id":"acme","administrator":"owner@example.com","application":"approvals","quotas":{"storage":null,"users":5}}""", post.Body);
        Assert.Equal("vn-1", await Scalar<string>("SELECT cell_id FROM directory.tenants WHERE id = 'acme'"));
        Assert.Equal("vn-1", await Scalar<string>("SELECT cell_id FROM directory.hosts WHERE host = 'acme.vn.example.com'"));
        Assert.Equal("acme", await Scalar<string>("SELECT tenant_id FROM directory.hosts WHERE host = 'acme.vn.example.com'"));

        var dnsHandler = new WriteTenantRecordJobHandler(_dataSource, _dns);
        await dnsHandler.RunAsync(await ClaimAsync(TenantRecordJobs.WriteKind), TestContext.Current.CancellationToken);
        var record = Assert.Single(_dns.Records);
        Assert.Equal("acme.vn.example.com", record.Key);
        Assert.Equal("ingress.vn-1.vn.example.com", record.Value.Target);
    }

    [Fact]
    public async Task A_blank_application_is_left_out_of_the_body_and_no_quota_is_sent_as_an_empty_object()
    {
        await Execute($"UPDATE control_plane.sign_ups SET application = '' WHERE id = '{_signUpId}'");
        var handler = HandlerWithQuotas(new Dictionary<string, long?>());

        await handler.RunAsync(Job(), TestContext.Current.CancellationToken);

        Assert.Equal("""{"id":"acme","administrator":"owner@example.com","quotas":{}}""", Assert.Single(_cells.Posts).Body);
    }

    [Fact]
    public async Task Running_the_job_twice_queues_one_tenantReady_email_and_the_sign_up_is_ready_and_the_second_run_asks_no_cell()
    {
        await RunAsync();
        await RunAsync();

        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail' AND payload->>'template' = 'tenantReady'"));
        Assert.Equal("owner@example.com", await Scalar<string>("SELECT payload->>'to' FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal("https://acme.vn.example.com", await Scalar<string>("SELECT payload->'values'->>'tenantAddress' FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal("ready", await Scalar<string>($"SELECT provisioning FROM control_plane.sign_ups WHERE id = '{_signUpId}'"));
        Assert.Single(_cells.Posts);
    }

    [Fact]
    public async Task A_run_that_finds_the_tenant_in_the_directory_asks_that_cell_again_with_the_same_key_and_body_and_not_another_cell()
    {
        await RunAsync();

        // As if the first run stopped after the cell accepted and before the sign-up was ready.
        await Execute($"UPDATE control_plane.sign_ups SET provisioning = 'creating' WHERE id = '{_signUpId}'");
        await Execute("DELETE FROM control_plane.jobs WHERE kind = 'sendEmail'");

        // vn-1 is now the fuller cell, so a new pick would choose vn-2.
        await Execute("UPDATE directory.cells SET tenant_count = 50 WHERE id = 'vn-1'");
        await RunAsync();

        Assert.Equal(["vn-1.test", "vn-1.test"], _cells.Posts.Select(post => post.Host));
        Assert.Equal(_cells.Posts[0].Key, _cells.Posts[1].Key);
        Assert.Equal(_cells.Posts[0].Body, _cells.Posts[1].Body);
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM directory.tenants WHERE id = 'acme'"));
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
    }

    [Fact]
    public async Task A_full_cell_is_marked_as_not_taking_new_tenants_and_the_next_cell_gets_the_tenant()
    {
        _cells.Full.Add("vn-1.test");

        await RunAsync();

        Assert.Equal(["vn-1.test", "vn-2.test"], _cells.Posts.Select(post => post.Host));
        Assert.Equal("vn-2", await Scalar<string>("SELECT cell_id FROM directory.tenants WHERE id = 'acme'"));
        Assert.False(await Scalar<bool>("SELECT takes_new_tenants FROM directory.cells WHERE id = 'vn-1'"));
        Assert.Equal("ready", await Scalar<string>($"SELECT provisioning FROM control_plane.sign_ups WHERE id = '{_signUpId}'"));
    }

    [Fact]
    public async Task With_every_cell_full_the_sign_up_is_queued_nothing_is_written_and_the_job_comes_back_in_5_minutes()
    {
        _cells.Full.Add("vn-1.test");
        _cells.Full.Add("vn-2.test");

        var before = DateTime.UtcNow;
        await RunAsync();
        var after = DateTime.UtcNow;

        Assert.Equal("queued", await Scalar<string>($"SELECT provisioning FROM control_plane.sign_ups WHERE id = '{_signUpId}'"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM directory.hosts"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'provisionTenant'"));
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = 'provisionTenant' AND payload = jsonb_build_object('signUpId', '{_signUpId}')"));
        Assert.InRange(await Scalar<DateTime>("SELECT next_attempt_at FROM control_plane.jobs WHERE kind = 'provisionTenant'"), before.AddMinutes(5), after.AddMinutes(5));
    }

    [Fact]
    public async Task A_sign_up_that_is_still_pending_makes_no_cell_call_and_writes_nothing()
    {
        await Execute($"UPDATE control_plane.sign_ups SET state = 'pending' WHERE id = '{_signUpId}'");

        await RunAsync();

        Assert.Empty(_cells.Posts);
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM directory.tenants"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.True(await Scalar<bool>($"SELECT provisioning IS NULL FROM control_plane.sign_ups WHERE id = '{_signUpId}'"));
    }

    [Fact]
    public async Task A_failed_cell_job_fails_the_run_with_a_text_that_holds_no_cell_answer_and_the_sign_up_is_not_ready_and_sends_no_email()
    {
        _cells.JobState = "failed";

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.DoesNotContain("acme", exception.Message, StringComparison.Ordinal);
        Assert.Equal("creating", await Scalar<string>($"SELECT provisioning FROM control_plane.sign_ups WHERE id = '{_signUpId}'"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
    }

    private Task RunAsync() => _handler.RunAsync(Job(), TestContext.Current.CancellationToken);

    private ClaimedJob Job() => new(Guid.CreateVersion7(), ProvisionTenantJobHandler.JobKind, new JsonObject { ["signUpId"] = _signUpId.ToString() }, 1);

    // The same handler on options with other quotas: the options are read when the job runs.
    private ProvisionTenantJobHandler HandlerWithQuotas(IReadOnlyDictionary<string, long?> quotas)
    {
        var controlPlane = new ControlPlaneOptions { Region = "vn", TenantDomain = "vn.example.com", PublicUrl = "https://account.vn.example.com", MailFrom = "no-reply@vn.example.com" };
        controlPlane.SignUp.TrialQuotas = quotas;
        return new ProvisionTenantJobHandler(
            _dataSource,
            _dataSource,
            new CellPicker(_dataSource, _dataSource, _loggers.CreateLogger<CellPicker>()),
            _clients,
            EmailTemplates.LoadEmbedded(),
            controlPlane,
            TimeProvider.System);
    }

    private Task<int> SeedSignUpAsync() => Execute($"""
        INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
        VALUES ('{_signUpId}', 'An Owner', 'owner@example.com', 'owner@example.com', 'Acme', 'acme', 'en', 'approvals', 'verified', now())
        """);

    private async Task<ClaimedJob> ClaimAsync(string kind)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return Assert.IsType<ClaimedJob>(await JobQueue.ClaimAsync(connection, "test", [kind], TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken));
    }

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> Scalar<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed record Post(string Host, string? Key, string Body);

    /// <summary>
    /// Stands in for the cells <c>vn-1</c> and <c>vn-2</c>: a token endpoint, the create route, which records each call and answers a
    /// queued job, or <c>409</c> "This cell is full." for a host in <see cref="Full"/>, and the job read, which answers
    /// <see cref="JobState"/>.
    /// </summary>
    private sealed class CellHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Post> _posts = new();

        public ConcurrentBag<string> Full { get; } = [];

        public string JobState { get; set; } = "succeeded";

        public IReadOnlyList<Post> Posts => [.. _posts];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/token")
            {
                return Json(new { access_token = "token", expires_in = 3600, token_type = "Bearer" });
            }

            if (request.Method == HttpMethod.Get && path.StartsWith("/api/cell/jobs/", StringComparison.Ordinal))
            {
                return Json(new { id = Guid.Parse(path["/api/cell/jobs/".Length..]), kind = "create", tenantId = "acme", state = JobState, createdAt = DateTimeOffset.UnixEpoch });
            }

            if (request.Method == HttpMethod.Post && path == "/api/cell/tenants")
            {
                var host = request.RequestUri.Host;
                _posts.Enqueue(new Post(host, request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return Full.Contains(host)
                    ? Json(new { title = CellClient.FullTitle, status = 409 }, HttpStatusCode.Conflict)
                    : Json(new { id = Guid.NewGuid(), kind = "create", tenantId = "acme", state = "queued", createdAt = DateTimeOffset.UnixEpoch }, HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = JsonContent.Create(body, options: JsonSerializerOptions.Web) };
    }
}
