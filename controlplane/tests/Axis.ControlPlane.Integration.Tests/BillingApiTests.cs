using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.BillingEvents;
using Axis.ControlPlane.Cells;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.RegionDirectory;
using Axis.ControlPlane.Subscriptions;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The billing API's paid-through call, plan call, end call and read on a real PostgreSQL, see <c>docs/reference/billing-api.md</c>. The billing client signs in with a token that a stub
/// identity provider's key signs, and the control plane checks it as it checks the operator's real one. The moves then run end to end: the call, the change
/// of state, and the jobs that write the plan's quotas to a cell that <see cref="CellStub"/> stands in for, and resume the tenant. The plan call's moves run to the cell the same way. The end call is followed by the clock, and the read gets its usage from the stub cell. Each test class instance has
/// a container of its own, with the region <c>vn</c> and the cell <c>vn-1</c>. Every tenant holds a marker of ten digits.
/// </summary>
public sealed class BillingApiTests : IAsyncLifetime
{
    private const string Authority = "https://idp.test/realms/billing";
    private const string Audience = "axis-control-plane";
    private const string BillingClient = "billing-client";
    private const string Tenant = "bill-4172903856";
    private const string OtherTenant = "bill-5938162047";
    private const string EventsUrl = "https://billing.example/events";
    private const string EventsKey = "billing-events-key";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
    private readonly CellStub _cell = new();
    private readonly RSA _key = RSA.Create(2048);
    private static readonly string _eventsSecret = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(EventsKey));

    private readonly DateOnly _paidThrough = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private ControlPlaneOptions _controlPlane = null!;
    private CellClients _clients = null!;
    private JobRunner _runner = null!;
    private SubscriptionClock _clock = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _http = null!;

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

        _controlPlane = new ControlPlaneOptions
        {
            TenantDomain = "vn.example.com",
            PublicUrl = "https://account.vn.example.com",
            MailFrom = "no-reply@vn.example.com",
        };
        _controlPlane.Cells.Add(new CellOptions { Id = "vn-1", InternalUrl = "https://vn-1.test", TokenUrl = "https://vn-1.test/token", ClientId = "axis-controlplane" });
        _controlPlane.CellSecrets["vn-1"] = "cell-secret";
        _clients = new CellClients(_controlPlane, new StubHttpClientFactory(_cell), new CellClientOptions(), TimeProvider.System);
        _loggers = LoggerFactory.Create(_ => { });
        var quotas = new SubscriptionQuotas(_dataSource, _dataSource, _clients, _catalogue, TimeProvider.System, _loggers.CreateLogger<SubscriptionQuotas>());
        var resume = new SubscriptionResume(_dataSource, _dataSource, _clients, _loggers.CreateLogger<SubscriptionResume>());
        _clock = new SubscriptionClock(_dataSource, _dataSource, _clients, EmailTemplates.LoadEmbedded(), _controlPlane, TimeProvider.System, _loggers.CreateLogger<SubscriptionClock>());
        _runner = new JobRunner(_dataSource, [quotas, resume], new JobRunnerOptions { BaseDelay = TimeSpan.Zero }, _loggers.CreateLogger<JobRunner>());

        // No cell is configured for the host, so only the test's runner claims the jobs.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", _container.GetConnectionString());
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", "127.0.0.1");
            builder.UseSetting("ControlPlane:Mail:Port", "1");
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
            builder.UseSetting("ControlPlane:Billing:Authority", Authority);
            builder.UseSetting("ControlPlane:Billing:Audience", Audience);
            builder.UseSetting("ControlPlane:Billing:ClientId", BillingClient);
            builder.ConfigureServices(services => services.RemoveAll<RecurringJob>());

            // The read finds a tenant's cell as the jobs do. The host's options list no cell, so it registers no quota or resume handler.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CellClients>();
                services.AddSingleton(_clients);
                services.AddKeyedSingleton("directory", _dataSource);
            });
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton(_catalogue)));
            builder.ConfigureServices(services => services.Configure<JwtBearerOptions>("Axis.Billing", jwt => jwt.BackchannelHttpHandler = new IdentityProvider(_key)));
        });
        _http = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _factory.DisposeAsync();
        _clients.Dispose();
        _loggers.Dispose();
        _cell.Dispose();
        _key.Dispose();
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task The_billing_client_sets_a_paid_through_date_on_a_trialing_subscription_and_it_becomes_active_with_its_quotas_written_to_the_cell()
    {
        var id = await SeedAsync(Tenant, "trialing");
        var before = await StepAsync(id);

        var response = await PutAsync(Tenant, "paid-through:inv-1", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = answer.RootElement;
        Assert.Equal(Tenant, root.GetProperty("tenantId").GetString());
        Assert.Equal("active", root.GetProperty("state").GetString());
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", root.GetProperty("paidThrough").GetString());
        Assert.Equal($"{_paidThrough.AddDays(1):yyyy-MM-dd}T00:00:00Z", root.GetProperty("nextAt").GetString());
        Assert.Equal("team", root.GetProperty("plan").GetProperty("code").GetString());
        Assert.Equal(1, root.GetProperty("plan").GetProperty("version").GetInt32());
        Assert.False(root.TryGetProperty("usage", out _));

        Assert.Equal("active", await StateAsync(id));
        Assert.NotEqual(before, await StepAsync(id));
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", await TextAsync($"SELECT to_char(paid_through, 'YYYY-MM-DD') FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal(
            $"{_paidThrough.AddDays(1):yyyy-MM-dd}T00:00",
            await TextAsync($"SELECT to_char(next_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI') FROM control_plane.subscriptions WHERE id = '{id}'"));
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("billing", BillingClient, "subscription.active"), (record.ActorKind, record.Actor, record.Action));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}'"));
        Assert.Empty(_cell.Calls);

        await DrainAsync();

        var put = Assert.Single(_cell.Puts);
        Assert.Equal($"/api/cell/tenants/{Tenant}/quotas", put.Path);
        Assert.Equal(25, put.Set[QuotaKeys.Users]);
        Assert.Equal(10_737_418_240, put.Set[QuotaKeys.Storage]);
        Assert.Empty(_cell.Resumes);
    }

    [Theory]
    [InlineData("readOnly")]
    [InlineData("pastDue")]
    public async Task The_same_call_on_a_read_only_or_past_due_subscription_queues_the_resume_and_the_cell_resumes_the_tenant_and_gets_its_quotas(string state)
    {
        var id = await SeedAsync(Tenant, state);
        var before = await StepAsync(id);

        var response = await PutAsync(Tenant, "paid-through:inv-2", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", await StateAsync(id));
        var step = await StepAsync(id);
        Assert.NotEqual(before, step);
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("billing", BillingClient, "subscription.active"), (record.ActorKind, record.Actor, record.Action));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}'"));
        Assert.Empty(_cell.Calls);

        await DrainAsync();

        var resume = Assert.Single(_cell.Resumes);
        Assert.Equal($"/api/cell/tenants/{Tenant}/resume", resume.Path);
        Assert.Equal($"resume:{step}", resume.Key);
        var put = Assert.Single(_cell.Puts);
        Assert.Equal(25, put.Set[QuotaKeys.Users]);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE state <> 'succeeded'"));
    }

    [Fact]
    public async Task A_past_due_subscription_that_the_clock_moved_returns_to_active_on_a_new_paid_through_date_and_the_cell_resumes_the_tenant()
    {
        var id = await SeedAsync(Tenant, "active", paidThrough: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        await _clock.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), TestContext.Current.CancellationToken);

        Assert.Equal("pastDue", await StateAsync(id));
        Assert.Empty(_cell.Calls);

        var response = await PutAsync(Tenant, "paid-through:back", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", await StateAsync(id));
        var step = await StepAsync(id);
        var records = await AuditAsync(id.ToString());
        Assert.Equal(["subscription.pastDue", "subscription.active"], records.Select(record => record.Action));
        Assert.Equal("job", records[0].ActorKind);
        Assert.Equal(("billing", BillingClient), (records[1].ActorKind, records[1].Actor));

        await DrainAsync();

        var resume = Assert.Single(_cell.Resumes);
        Assert.Equal($"resume:{step}", resume.Key);
    }

    [Fact]
    public async Task A_cancelled_subscription_becomes_active_and_gets_its_quotas_but_no_resume()
    {
        var id = await SeedAsync(Tenant, "cancelled");

        var response = await PutAsync(Tenant, "paid-through:inv-3", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", await StateAsync(id));
        Assert.Equal("subscription.active", Assert.Single(await AuditAsync(id.ToString())).Action);

        await DrainAsync();

        Assert.Equal($"PUT /api/cell/tenants/{Tenant}/quotas", Assert.Single(_cell.Calls));
    }

    [Fact]
    public async Task An_active_subscription_keeps_its_state_and_step_takes_the_new_date_and_queues_nothing()
    {
        var id = await SeedAsync(Tenant, "active");
        var before = await StepAsync(id);

        var response = await PutAsync(Tenant, "paid-through:inv-4", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", await StateAsync(id));
        Assert.Equal(before, await StepAsync(id));
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", await TextAsync($"SELECT to_char(paid_through, 'YYYY-MM-DD') FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal("subscription.paidThroughSet", Assert.Single(await AuditAsync(id.ToString())).Action);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
    }

    [Fact]
    public async Task A_call_with_no_token_or_another_clients_token_or_a_wrong_audience_answers_401_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "readOnly");
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";

        var none = await PutAsync(Tenant, "paid-through:none", body, null);
        var other = await PutAsync(Tenant, "paid-through:other", body, Token(client: "another-client"));
        var audience = await PutAsync(Tenant, "paid-through:audience", body, Token(audience: "another-audience"));
        var forged = await PutAsync(Tenant, "paid-through:forged", body, Token(signedWith: RSA.Create(2048)));

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("Bearer", Assert.Single(none.Headers.WwwAuthenticate).ToString());
        foreach (var response in new[] { other, audience, forged })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        }

        await AssertUnchangedAsync(id, "readOnly");
    }

    [Fact]
    public async Task A_token_of_the_billing_client_without_the_billing_scope_answers_403_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "pastDue");
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";

        var none = await PutAsync(Tenant, "paid-through:noscope", body, Token(scope: null));
        var wrong = await PutAsync(Tenant, "paid-through:wrongscope", body, Token(scope: "openid profile"));

        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        await AssertUnchangedAsync(id, "pastDue");
    }

    [Fact]
    public async Task A_token_that_names_the_client_in_azp_and_holds_the_scope_among_others_is_accepted()
    {
        var id = await SeedAsync(Tenant, "trialing");

        var response = await PutAsync(Tenant, "paid-through:azp", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token(clientClaim: "azp", scope: "openid axis-billing"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("active", await StateAsync(id));
    }

    [Fact]
    public async Task The_same_key_and_body_twice_answer_the_same_and_act_once_and_the_same_key_with_another_body_answers_422()
    {
        var id = await SeedAsync(Tenant, "readOnly");
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";

        var first = await PutAsync(Tenant, "paid-through:inv-5", body, Token());
        var second = await PutAsync(Tenant, "paid-through:inv-5", body, Token());
        var another = await PutAsync(Tenant, "paid-through:inv-5", $$"""{"paidThrough":"{{_paidThrough.AddDays(1):yyyy-MM-dd}}"}""", Token());
        var otherTenant = await SeedAsync(OtherTenant, "readOnly");
        var elsewhere = await PutAsync(OtherTenant, "paid-through:inv-5", body, Token());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, another.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, elsewhere.StatusCode);
        Assert.Equal("readOnly", await StateAsync(otherTenant));
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", await TextAsync($"SELECT to_char(paid_through, 'YYYY-MM-DD') FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Single(await AuditAsync(id.ToString()));
        Assert.Empty(await AuditAsync(otherTenant.ToString()));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionResume.JobKind}'"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Fact]
    public async Task Parallel_retries_with_the_same_key_act_once()
    {
        var id = await SeedAsync(Tenant, "pastDue");
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PutAsync(Tenant, "paid-through:parallel", body, Token())));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
    }

    [Fact]
    public async Task A_key_used_more_than_24_hours_ago_acts_again()
    {
        var id = await SeedAsync(Tenant, "active");
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(Tenant, "paid-through:old", body, Token())).StatusCode);
        await Execute("UPDATE control_plane.billing_requests SET created_at = now() - interval '25 hours'");

        var again = await PutAsync(Tenant, "paid-through:old", $$"""{"paidThrough":"{{_paidThrough.AddDays(2):yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await AuditAsync(id.ToString())).Count);
    }

    [Fact]
    public async Task A_missing_key_an_unknown_tenant_a_subscription_with_no_plan_and_a_past_date_each_change_nothing_and_write_no_audit_record()
    {
        var id = await SeedAsync(Tenant, "trialing");
        var noPlan = await SeedAsync(OtherTenant, "trialing", pinsPlan: false);
        var body = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-2);

        var missing = await PutAsync(Tenant, null, body, Token());
        var unknown = await PutAsync("bill-0000000000", "paid-through:unknown", body, Token());
        var withoutPlan = await PutAsync(OtherTenant, "paid-through:no-plan", body, Token());
        var yesterday = await PutAsync(Tenant, "paid-through:past", $$"""{"paidThrough":"{{past:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, withoutPlan.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, yesterday.StatusCode);
        await AssertUnchangedAsync(id, "trialing");
        await AssertUnchangedAsync(noPlan, "trialing");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"paidThrough":20270131}""")]
    [InlineData("""{"paidThrough":"31/01/2027"}""")]
    [InlineData("""{"paidThrough":"2027-02-30"}""")]
    [InlineData("[]")]
    public async Task A_body_that_does_not_parse_or_holds_a_malformed_date_answers_400_keyed_by_the_field_and_changes_nothing(string body)
    {
        var id = await SeedAsync(Tenant, "trialing");

        var response = await PutAsync(Tenant, "paid-through:body", body, Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("paidThrough", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await AssertUnchangedAsync(id, "trialing");
    }

    [Theory]
    [InlineData("deleting")]
    [InlineData("deleted")]
    public async Task A_deleting_or_deleted_subscription_answers_409_and_changes_nothing(string state)
    {
        var id = await SeedAsync(Tenant, state);

        var response = await PutAsync(Tenant, "paid-through:deleting", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertUnchangedAsync(id, state);
    }

    [Fact]
    public async Task The_billing_client_sets_team_version_2_with_two_user_packs_and_the_cell_receives_its_quotas_with_the_users_raised()
    {
        var id = await SeedAsync(Tenant, "active");
        var step = await StepAsync(id);

        var response = await PutPlanAsync(Tenant, "plan:inv-1", Team(2, ("users-10", 2)), Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = answer.RootElement;
        Assert.Equal(Tenant, root.GetProperty("tenantId").GetString());
        Assert.Equal("active", root.GetProperty("state").GetString());
        Assert.Equal("team", root.GetProperty("plan").GetProperty("code").GetString());
        Assert.Equal(2, root.GetProperty("plan").GetProperty("version").GetInt32());
        var addOn = Assert.Single(root.GetProperty("addOns").EnumerateArray());
        Assert.Equal("users-10", addOn.GetProperty("code").GetString());
        Assert.Equal(2, addOn.GetProperty("quantity").GetInt32());

        Assert.Equal(2, await ScalarAsync<int>($"SELECT plan_version FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal("active", await StateAsync(id));
        Assert.Equal(step, await StepAsync(id));
        Assert.Equal("users-10:2", await TextAsync($"SELECT add_on_code || ':' || quantity FROM control_plane.subscription_add_ons WHERE subscription_id = '{id}'"));
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("billing", BillingClient, "subscription.planSet"), (record.ActorKind, record.Actor, record.Action));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        Assert.Empty(_cell.Calls);

        await DrainAsync();

        var put = Assert.Single(_cell.Puts);
        Assert.Equal($"/api/cell/tenants/{Tenant}/quotas", put.Path);
        Assert.Equal(25 + 20, put.Set[QuotaKeys.Users]);
        Assert.Equal(21_474_836_480, put.Set[QuotaKeys.Storage]);
    }

    [Fact]
    public async Task A_second_plan_call_with_no_packs_drops_the_packs_and_the_cell_receives_the_included_users()
    {
        var id = await SeedAsync(Tenant, "active");
        Assert.Equal(HttpStatusCode.OK, (await PutPlanAsync(Tenant, "plan:inv-2", Team(2, ("users-10", 2)), Token())).StatusCode);

        var response = await PutPlanAsync(Tenant, "plan:inv-3", Team(2), Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.subscription_add_ons WHERE subscription_id = '{id}'"));
        Assert.Equal(2, (await AuditAsync(id.ToString())).Count);

        await DrainAsync();

        Assert.Equal(25, _cell.Puts[^1].Set[QuotaKeys.Users]);
    }

    [Fact]
    public async Task A_plan_call_with_the_same_values_still_writes_a_record_and_queues_the_quotas_and_a_trial_takes_the_plan_at_once()
    {
        var id = await SeedAsync(Tenant, "trialing");

        var response = await PutPlanAsync(Tenant, "plan:same", Team(1), Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("trialing", await StateAsync(id));
        Assert.Equal("subscription.planSet", Assert.Single(await AuditAsync(id.ToString())).Action);
        await DrainAsync();
        Assert.Equal(25, Assert.Single(_cell.Puts).Set[QuotaKeys.Users]);
    }

    [Theory]
    [InlineData("""{"code":"unknown","version":1,"addOns":[]}""")]
    [InlineData("""{"code":"team","version":9,"addOns":[]}""")]
    [InlineData("""{"code":"trial","version":1,"addOns":[]}""")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-50","quantity":1}]}""")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-10","quantity":0}]}""")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-10","quantity":-1}]}""")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-10","quantity":1},{"code":"users-10","quantity":2}]}""")]
    public async Task A_plan_or_add_on_that_the_catalogue_does_not_list_or_a_trial_plan_or_a_bad_quantity_or_a_repeated_code_answers_422_and_changes_nothing(string body)
    {
        var id = await SeedAsync(Tenant, "active");

        var response = await PutAsync(Tenant, "plan:refused", body, Token(), "plan");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertPlanUnchangedAsync(id, "active");
    }

    [Fact]
    public async Task A_plan_call_with_no_token_answers_401_and_one_without_the_billing_scope_answers_403_and_neither_changes_anything()
    {
        var id = await SeedAsync(Tenant, "active");
        var body = Team(2, ("users-10", 1));

        var none = await PutPlanAsync(Tenant, "plan:none", body, null);
        var noScope = await PutPlanAsync(Tenant, "plan:noscope", body, Token(scope: null));
        var wrongScope = await PutPlanAsync(Tenant, "plan:wrongscope", body, Token(scope: "openid profile"));

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("Bearer", Assert.Single(none.Headers.WwwAuthenticate).ToString());
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
        await AssertPlanUnchangedAsync(id, "active");
    }

    [Theory]
    [InlineData("not json", "code")]
    [InlineData("[]", "code")]
    [InlineData("""{"version":2,"addOns":[]}""", "code")]
    [InlineData("""{"code":"","version":2,"addOns":[]}""", "code")]
    [InlineData("""{"code":7,"version":2,"addOns":[]}""", "code")]
    [InlineData("""{"code":"team","addOns":[]}""", "version")]
    [InlineData("""{"code":"team","version":"2","addOns":[]}""", "version")]
    [InlineData("""{"code":"team","version":2.5,"addOns":[]}""", "version")]
    [InlineData("""{"code":"team","version":2}""", "addOns")]
    [InlineData("""{"code":"team","version":2,"addOns":{}}""", "addOns")]
    [InlineData("""{"code":"team","version":2,"addOns":["users-10"]}""", "addOns")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"quantity":1}]}""", "addOns")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-10","quantity":"1"}]}""", "addOns")]
    [InlineData("""{"code":"team","version":2,"addOns":[{"code":"users-10","quantity":1.5}]}""", "addOns")]
    public async Task A_plan_body_that_does_not_parse_or_holds_a_malformed_value_answers_400_keyed_by_the_field_and_changes_nothing(string body, string field)
    {
        var id = await SeedAsync(Tenant, "active");

        var response = await PutAsync(Tenant, "plan:body", body, Token(), "plan");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        await AssertPlanUnchangedAsync(id, "active");
    }

    [Fact]
    public async Task A_plan_call_with_no_key_or_for_an_unknown_tenant_answers_400_or_404_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "active");

        var missing = await PutPlanAsync(Tenant, null, Team(2), Token());
        var unknown = await PutPlanAsync("bill-0000000000", "plan:unknown", Team(2), Token());

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await AssertPlanUnchangedAsync(id, "active");
    }

    [Theory]
    [InlineData("deleting")]
    [InlineData("deleted")]
    public async Task A_plan_call_on_a_deleting_or_deleted_subscription_answers_409_and_changes_nothing(string state)
    {
        var id = await SeedAsync(Tenant, state);

        var response = await PutPlanAsync(Tenant, "plan:deleting", Team(2, ("users-10", 1)), Token());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertPlanUnchangedAsync(id, state);
    }

    [Fact]
    public async Task A_downgrade_that_the_use_does_not_fit_answers_409_names_each_quota_over_and_changes_nothing_and_the_same_key_acts_once_the_use_fits()
    {
        var id = await SeedAsync(Tenant, "active");
        await Execute($"UPDATE control_plane.subscriptions SET plan_version = 2 WHERE id = '{id}'");
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{id}', 'users-10', 2)");
        _cell.Usage = new
        {
            storage = new { limit = 21_474_836_480L, used = 16_106_127_360L },
            users = new { limit = 45L, used = 32L },
            applications = new { limit = 5L, used = 5L },
            emailsPerDay = new { limit = 2000L, used = 2000L },
        };

        var refused = await PutPlanAsync(Tenant, "plan:down", Team(1), Token());

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            var quotas = problem.RootElement.GetProperty("quotas").EnumerateArray().ToDictionary(item => item.GetProperty("quota").GetString()!);
            Assert.Equal(["storage", "users"], quotas.Keys.Order());
            Assert.Equal((10_737_418_240L, 16_106_127_360L, 5_368_709_120L), LimitUsedOver(quotas["storage"]));
            Assert.Equal((25L, 32L, 7L), LimitUsedOver(quotas["users"]));
        }

        Assert.Equal(2, await ScalarAsync<int>($"SELECT plan_version FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal("users-10:2", await TextAsync($"SELECT add_on_code || ':' || quantity FROM control_plane.subscription_add_ons WHERE subscription_id = '{id}'"));
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
        Assert.Empty(_cell.Puts);

        _cell.Usage = new
        {
            storage = new { limit = 21_474_836_480L, used = 1_048_576L },
            users = new { limit = 45L, used = 20L },
            applications = new { limit = 5L, used = 5L },
            emailsPerDay = new { limit = 2000L, used = 2000L },
        };

        var retried = await PutPlanAsync(Tenant, "plan:down", Team(1), Token());

        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal(1, await ScalarAsync<int>($"SELECT plan_version FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal("subscription.planSet", Assert.Single(await AuditAsync(id.ToString())).Action);
    }

    [Fact]
    public async Task A_plan_that_raises_the_quota_the_tenant_is_already_over_answers_200_and_queues_the_quotas()
    {
        await SeedAsync(Tenant, "active");
        _cell.Usage = new
        {
            storage = new { limit = 10_737_418_240L, used = 16_106_127_360L },
            users = new { limit = 25L, used = 12L },
            applications = new { limit = 5L, used = 1L },
            emailsPerDay = new { limit = 2000L, used = 40L },
        };

        var response = await PutPlanAsync(Tenant, "plan:up", Team(2), Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
    }

    [Fact]
    public async Task A_plan_call_whose_cell_read_fails_answers_502_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "active");
        _cell.FailReads = true;

        var response = await PutPlanAsync(Tenant, "plan:unreadable", Team(2), Token());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        await AssertPlanUnchangedAsync(id, "active");
        Assert.Empty(_cell.Puts);
    }

    [Fact]
    public async Task The_same_key_and_body_twice_on_the_plan_call_answer_the_same_and_write_one_record_and_one_job_and_the_same_key_on_another_route_answers_422()
    {
        var id = await SeedAsync(Tenant, "active");
        var body = Team(2, ("users-10", 2));

        var first = await PutPlanAsync(Tenant, "plan:inv-9", body, Token());
        var second = await PutPlanAsync(Tenant, "plan:inv-9", body, Token());
        var other = await PutAsync(Tenant, "plan:inv-9", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, other.StatusCode);
        Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(1L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.jobs WHERE kind = '{SubscriptionQuotas.JobKind}'"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Fact]
    public async Task A_plan_set_on_a_trial_that_pins_none_and_then_a_paid_through_date_make_it_active_and_the_cell_receives_the_plans_quotas()
    {
        var id = await SeedAsync(Tenant, "trialing", pinsPlan: false);

        var plan = await PutPlanAsync(Tenant, "plan:trial", Team(2, ("users-10", 1)), Token());
        var paid = await PutAsync(Tenant, "paid-through:trial", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal("active", await StateAsync(id));
        Assert.Equal(
            ["subscription.active", "subscription.planSet"],
            (await AuditAsync(id.ToString())).Select(record => record.Action).Order(StringComparer.Ordinal).ToArray());

        await DrainAsync();

        Assert.All(_cell.Puts, put => Assert.Equal(35, put.Set[QuotaKeys.Users]));
        Assert.Equal(21_474_836_480, _cell.Puts[^1].Set[QuotaKeys.Storage]);
    }

    [Fact]
    public async Task Ending_an_active_subscription_makes_it_cancelled_with_one_record_by_the_billing_client_and_the_clock_makes_it_read_only_after_its_paid_through_date()
    {
        var id = await SeedAsync(Tenant, "active", paidThrough: DateOnly.FromDateTime(DateTime.UtcNow));
        var before = await StepAsync(id);
        var nextAt = await TextAsync($"SELECT next_at::text FROM control_plane.subscriptions WHERE id = '{id}'");

        var response = await PostAsync(Tenant, "end:inv-1", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.Equal(Tenant, answer.RootElement.GetProperty("tenantId").GetString());
            Assert.Equal("cancelled", answer.RootElement.GetProperty("state").GetString());
            Assert.Equal($"{DateTime.UtcNow:yyyy-MM-dd}", answer.RootElement.GetProperty("paidThrough").GetString());
            Assert.False(answer.RootElement.TryGetProperty("usage", out _));
        }

        Assert.Equal("cancelled", await StateAsync(id));
        var step = await StepAsync(id);
        Assert.NotEqual(before, step);
        Assert.Equal(nextAt, await TextAsync($"SELECT next_at::text FROM control_plane.subscriptions WHERE id = '{id}'"));
        var record = Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(("billing", BillingClient, "subscription.cancelled"), (record.ActorKind, record.Actor, record.Action));

        // The tenant works until the day after its paid-through date: the clock has nothing due yet.
        await _clock.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), TestContext.Current.CancellationToken);

        Assert.Equal("cancelled", await StateAsync(id));
        Assert.Empty(_cell.Calls);

        await Execute($"UPDATE control_plane.subscriptions SET paid_through = paid_through - 2, next_at = next_at - interval '2 days' WHERE id = '{id}'");
        await _clock.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), TestContext.Current.CancellationToken);

        Assert.Equal("readOnly", await StateAsync(id));
        var readOnly = Assert.Single(_cell.ReadOnlys);
        Assert.Equal($"/api/cell/tenants/{Tenant}/read-only", readOnly.Path);
        Assert.Equal($"read-only:{step}", readOnly.Key);
        var records = await AuditAsync(id.ToString());
        Assert.Equal(["subscription.cancelled", "subscription.readOnly"], records.Select(record => record.Action));
        Assert.Equal(("billing", "job"), (records[0].ActorKind, records[1].ActorKind));
    }

    [Fact]
    public async Task Ending_a_cancelled_subscription_answers_200_and_writes_nothing()
    {
        var id = await SeedAsync(Tenant, "cancelled");
        var step = await StepAsync(id);

        var response = await PostAsync(Tenant, "end:again", Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("cancelled", answer.RootElement.GetProperty("state").GetString());
        Assert.Equal(step, await StepAsync(id));
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Theory]
    [InlineData("trialing")]
    [InlineData("pastDue")]
    [InlineData("readOnly")]
    [InlineData("deleting")]
    [InlineData("deleted")]
    public async Task Ending_a_subscription_that_is_not_active_or_cancelled_answers_409_and_changes_nothing(string state)
    {
        var id = await SeedAsync(Tenant, state);
        var step = await StepAsync(id);

        var response = await PostAsync(Tenant, "end:refused", Token());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(step, await StepAsync(id));
        await AssertUnchangedAsync(id, state);
    }

    [Fact]
    public async Task Ending_without_a_key_answers_400_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "active");

        var response = await PostAsync(Tenant, null, Token());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUnchangedAsync(id, "active");
    }

    [Fact]
    public async Task The_same_key_sent_twice_to_the_end_call_answers_the_same_text_and_writes_one_record_and_a_key_first_used_on_another_call_answers_422()
    {
        var id = await SeedAsync(Tenant, "active");
        var other = await SeedAsync(OtherTenant, "readOnly");

        var first = await PostAsync(Tenant, "end:inv-2", Token());
        var second = await PostAsync(Tenant, "end:inv-2", Token());
        var paidThrough = await PutAsync(OtherTenant, "shared-key", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token());
        var reused = await PostAsync(OtherTenant, "shared-key", Token());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(await AuditAsync(id.ToString()));
        Assert.Equal(HttpStatusCode.OK, paidThrough.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
        Assert.Equal("active", await StateAsync(other));
        Assert.Equal("subscription.active", Assert.Single(await AuditAsync(other.ToString())).Action);
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Fact]
    public async Task Reading_a_tenant_returns_its_state_dates_plan_add_ons_and_the_usage_of_its_cell_and_writes_no_record()
    {
        var id = await SeedAsync(Tenant, "active", paidThrough: _paidThrough);
        await Execute($"INSERT INTO control_plane.subscription_add_ons (subscription_id, add_on_code, quantity) VALUES ('{id}', 'users-10', 2)");
        _cell.Usage = new
        {
            storage = new { limit = 10_737_418_240L, used = 1_048_576L },
            users = new { limit = 45L, used = 12L },
            applications = new { limit = 5L, used = (long?)null },
            emailsPerDay = new { limit = 2000L, used = 40L },
        };

        var response = await GetAsync(Tenant, Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = answer.RootElement;
        Assert.Equal(Tenant, root.GetProperty("tenantId").GetString());
        Assert.Equal("active", root.GetProperty("state").GetString());
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", root.GetProperty("paidThrough").GetString());
        Assert.Equal($"{_paidThrough.AddDays(1):yyyy-MM-dd}T00:00:00Z", root.GetProperty("nextAt").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("trialEndsAt").ValueKind);
        Assert.Equal(("team", 1), (root.GetProperty("plan").GetProperty("code").GetString(), root.GetProperty("plan").GetProperty("version").GetInt32()));
        var addOn = Assert.Single(root.GetProperty("addOns").EnumerateArray());
        Assert.Equal(("users-10", 2), (addOn.GetProperty("code").GetString(), addOn.GetProperty("quantity").GetInt32()));
        var usage = root.GetProperty("usage");
        Assert.Equal((10_737_418_240L, 1_048_576L), (usage.GetProperty("storage").GetProperty("limit").GetInt64(), usage.GetProperty("storage").GetProperty("used").GetInt64()));
        Assert.Equal((45L, 12L), (usage.GetProperty("users").GetProperty("limit").GetInt64(), usage.GetProperty("users").GetProperty("used").GetInt64()));
        Assert.Equal(5L, usage.GetProperty("applications").GetProperty("limit").GetInt64());
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("applications").GetProperty("used").ValueKind);
        Assert.Equal((2000L, 40L), (usage.GetProperty("emailsPerDay").GetProperty("limit").GetInt64(), usage.GetProperty("emailsPerDay").GetProperty("used").GetInt64()));
        Assert.Equal($"/api/cell/tenants/{Tenant}", Assert.Single(_cell.Reads));
        Assert.Empty(_cell.Calls);
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Fact]
    public async Task Reading_a_deleted_subscription_returns_null_usage_and_does_not_call_the_cell()
    {
        await SeedAsync(Tenant, "deleted", pinsPlan: false);

        var response = await GetAsync(Tenant, Token());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("deleted", answer.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("usage").ValueKind);
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("plan").ValueKind);
        Assert.Empty(_cell.Reads);
    }

    [Fact]
    public async Task Reading_a_tenant_whose_cell_fails_or_is_unknown_answers_502_and_changes_nothing()
    {
        var id = await SeedAsync(Tenant, "active");
        _cell.FailReads = true;

        var failed = await GetAsync(Tenant, Token());

        _cell.FailReads = false;
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('vn-2', 'vn', 'https://vn-2.test', 100, 1)");
        await Execute($"UPDATE directory.tenants SET cell_id = 'vn-2' WHERE id = '{Tenant}'");
        var elsewhere = await GetAsync(Tenant, Token());

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Equal(HttpStatusCode.BadGateway, elsewhere.StatusCode);
        Assert.Single(_cell.Reads);
        Assert.Empty(await AuditAsync(id.ToString()));
    }

    [Fact]
    public async Task The_end_call_and_the_read_with_no_token_answer_401_and_with_a_token_without_the_billing_scope_answer_403_and_change_nothing()
    {
        var id = await SeedAsync(Tenant, "active");

        var endNone = await PostAsync(Tenant, "end:none", null);
        var readNone = await GetAsync(Tenant, null);
        var endScope = await PostAsync(Tenant, "end:scope", Token(scope: null));
        var readScope = await GetAsync(Tenant, Token(scope: "openid profile"));

        foreach (var response in new[] { endNone, readNone })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).ToString());
        }

        Assert.Equal(HttpStatusCode.Forbidden, endScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readScope.StatusCode);
        Assert.Empty(_cell.Reads);
        await AssertUnchangedAsync(id, "active");
    }

    [Fact]
    public async Task Ending_or_reading_an_unknown_tenant_or_one_that_another_region_holds_answers_404()
    {
        await Execute("INSERT INTO directory.regions (code, tenant_domain, api_url) VALUES ('sg', 'sg.example.com', 'https://api.sg.example.com')");
        await Execute("INSERT INTO directory.cells (id, region_code, internal_url, cap, tenant_count) VALUES ('sg-1', 'sg', 'https://sg-1.test', 100, 1)");
        await Execute("INSERT INTO directory.tenants (id, region_code, cell_id) VALUES ('bill-7351860294', 'sg', 'sg-1')");

        var unknown = await GetAsync("bill-0000000000", Token());
        var elsewhere = await GetAsync("bill-7351860294", Token());
        var endElsewhere = await PostAsync("bill-7351860294", "end:elsewhere", Token());

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, endElsewhere.StatusCode);
        Assert.Empty(_cell.Reads);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    [Fact]
    public async Task A_paid_through_call_that_makes_a_trialing_subscription_active_delivers_one_signed_state_event_that_names_both_states()
    {
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        using var factory = EventsFactory(receiver);
        using var client = factory.CreateClient();
        await SeedAsync(Tenant, "trialing");

        var response = await PutAsync(Tenant, "paid-through:event", $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""", Token(), client: client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitAsync(async () => await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent' AND state = 'succeeded'") == 1);
        var request = Assert.Single(receiver.Requests);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("subscription.stateChanged", body["type"]!.GetValue<string>());
        Assert.Equal(body["id"]!.GetValue<string>(), request.Headers["webhook-id"]);
        var data = body["data"]!.AsObject();
        Assert.Equal(Tenant, data["tenantId"]!.GetValue<string>());
        Assert.Equal("active", data["state"]!.GetValue<string>());
        Assert.Equal("trialing", data["previousState"]!.GetValue<string>());
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", data["paidThrough"]!.GetValue<string>());
        Assert.Equal($"{_paidThrough.AddDays(1):yyyy-MM-dd}T00:00:00.000Z", data["nextAt"]!.GetValue<string>());
        Assert.Equal(request.ExpectedSignature(Encoding.UTF8.GetBytes(EventsKey)), request.Headers["webhook-signature"]);
    }

    [Fact]
    public async Task A_clock_run_that_moves_an_active_subscription_to_past_due_delivers_one_state_event_with_both_states_and_the_new_next_date()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        var paidThrough = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var id = await SeedAsync(Tenant, "active", paidThrough: paidThrough);
        var options = new ControlPlaneOptions
        {
            TenantDomain = _controlPlane.TenantDomain,
            PublicUrl = _controlPlane.PublicUrl,
            MailFrom = _controlPlane.MailFrom,
            BillingEvents = new ControlPlaneBillingEventsOptions { Url = EventsUrl, Secret = _eventsSecret },
        };
        var clock = new SubscriptionClock(_dataSource, _dataSource, _clients, EmailTemplates.LoadEmbedded(), options, TimeProvider.System, _loggers.CreateLogger<SubscriptionClock>());
        var handler = new SendBillingEventJobHandler(_dataSource, options, new StubHttpClientFactory(receiver), TimeProvider.System);
        var runner = new JobRunner(_dataSource, [handler], new JobRunnerOptions { BaseDelay = TimeSpan.Zero }, _loggers.CreateLogger<JobRunner>());

        await clock.RunAsync(new ClaimedJob(Guid.NewGuid(), SubscriptionClock.JobKind, [], 1), cancellation);
        while (await runner.RunOnceAsync(cancellation))
        {
        }

        Assert.Equal("pastDue", await StateAsync(id));
        var request = Assert.Single(receiver.Requests);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("subscription.stateChanged", body["type"]!.GetValue<string>());
        var data = body["data"]!.AsObject();
        Assert.Equal(Tenant, data["tenantId"]!.GetValue<string>());
        Assert.Equal("pastDue", data["state"]!.GetValue<string>());
        Assert.Equal("active", data["previousState"]!.GetValue<string>());
        Assert.Equal($"{paidThrough:yyyy-MM-dd}", data["paidThrough"]!.GetValue<string>());
        var nextAt = await TextAsync($"SELECT to_char(next_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.MS\"Z\"') FROM control_plane.subscriptions WHERE id = '{id}'");
        Assert.Equal(nextAt, data["nextAt"]!.GetValue<string>());
        Assert.Equal(request.ExpectedSignature(Encoding.UTF8.GetBytes(EventsKey)), request.Headers["webhook-signature"]);
    }

    [Fact]
    public async Task A_plan_call_a_paid_through_call_on_an_active_subscription_and_a_call_without_a_token_queue_no_state_event()
    {
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        using var factory = EventsFactory(receiver);
        using var client = factory.CreateClient();
        var trialing = await SeedAsync(Tenant, "trialing");
        var active = await SeedAsync(OtherTenant, "active");
        var paidThrough = $$"""{"paidThrough":"{{_paidThrough:yyyy-MM-dd}}"}""";

        var plan = await PutAsync(Tenant, "plan:event", Team(1), Token(), "plan", client);
        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));

        var kept = await PutAsync(OtherTenant, "paid-through:kept", paidThrough, Token(), client: client);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        Assert.Equal("active", await StateAsync(active));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));

        var refused = await PutAsync(Tenant, "paid-through:none", paidThrough, null, client: client);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("trialing", await StateAsync(trialing));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
        Assert.Empty(receiver.Requests);
    }

    [Fact]
    public async Task Ending_an_active_subscription_queues_one_state_event_from_active_to_cancelled()
    {
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        using var factory = EventsFactory(receiver);
        using var client = factory.CreateClient();
        await SeedAsync(Tenant, "active", paidThrough: _paidThrough);

        var response = await PostAsync(Tenant, "end:event", Token(), client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await WaitAsync(async () => await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent' AND state = 'succeeded'") == 1);
        var data = JsonNode.Parse(Assert.Single(receiver.Requests).Body)!["data"]!.AsObject();
        Assert.Equal(("cancelled", "active"), (data["state"]!.GetValue<string>(), data["previousState"]!.GetValue<string>()));
        Assert.Equal($"{_paidThrough:yyyy-MM-dd}", data["paidThrough"]!.GetValue<string>());
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
    }

    private async Task AssertPlanUnchangedAsync(Guid id, string state)
    {
        Assert.Equal(state, await StateAsync(id));
        Assert.Equal(1, await ScalarAsync<int>($"SELECT plan_version FROM control_plane.subscriptions WHERE id = '{id}'"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.subscription_add_ons"));
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    private static (long Limit, long Used, long Over) LimitUsedOver(JsonElement quota) =>
        (quota.GetProperty("limit").GetInt64(), quota.GetProperty("used").GetInt64(), quota.GetProperty("over").GetInt64());

    private static string Team(int version, params (string Code, int Quantity)[] addOns) =>
        JsonSerializer.Serialize(new { code = "team", version, addOns = addOns.Select(a => new { code = a.Code, quantity = a.Quantity }) });

    private Task<HttpResponseMessage> PutPlanAsync(string tenant, string? key, string body, string? token) => PutAsync(tenant, key, body, token, "plan");

    private async Task AssertUnchangedAsync(Guid id, string state)
    {
        Assert.Equal(state, await StateAsync(id));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.subscriptions WHERE id = '{id}' AND paid_through IS NOT NULL"));
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    private async Task<HttpResponseMessage> PutAsync(string tenant, string? key, string body, string? token, string route = "paid-through", HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/billing/v1/tenants/{tenant}/subscription/{route}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await (client ?? _http).SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(string tenant, string? key, string? token, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/billing/v1/tenants/{tenant}/subscription/end");
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await (client ?? _http).SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> GetAsync(string tenant, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/billing/v1/tenants/{tenant}/subscription");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // A token of the stub identity provider's key, by default of the billing client with the billing scope.
    private string Token(string client = BillingClient, string? scope = "axis-billing", string audience = Audience, string clientClaim = "client_id", RSA? signedWith = null)
    {
        var claims = new Dictionary<string, object> { [clientClaim] = client, ["sub"] = $"service-account-{client}" };
        if (scope is not null)
        {
            claims["scope"] = scope;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signedWith ?? _key) { KeyId = IdentityProvider.KeyId }, SecurityAlgorithms.RsaSha256),
        });
    }

    // The host with the billing events address set, sending to the stub receiver. It polls fast, so a test waits only for the event job. The other
    // tests keep the address unset, so their job counts stay exact.
    private WebApplicationFactory<Program> EventsFactory(ReceiverStub receiver) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ControlPlane:BillingEvents:Url", EventsUrl);
            builder.UseSetting("ControlPlane:BillingEvents:Secret", _eventsSecret);
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new JobRunnerOptions { PollInterval = TimeSpan.FromMilliseconds(20) });
                services.AddHttpClient(SendBillingEventJobHandler.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver);
            });
        });

    private static async Task WaitAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 60 seconds.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private async Task DrainAsync()
    {
        while (await _runner.RunOnceAsync(TestContext.Current.CancellationToken))
        {
        }
    }

    private async Task<IReadOnlyList<AuditRecord>> AuditAsync(string subject)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return await AuditRecords.ReadAsync(connection, subject, TestContext.Current.CancellationToken);
    }

    // A subscription of a verified sign-up that pins version 1 of the team plan, or none. A new trial pins no plan. With a paidThrough it is due the day after.
    private async Task<Guid> SeedAsync(string tenant, string state, bool pinsPlan = true, DateOnly? paidThrough = null)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        var nextAt = paidThrough is { } date ? $"'{date.AddDays(1):yyyy-MM-dd}T00:00:00Z'::timestamptz" : "now()";
        await Execute($"""
            INSERT INTO control_plane.subscriptions
                (id, tenant_id, sign_up_id, state, trial_ends_at, next_at, step_id{(pinsPlan ? ", plan_code, plan_version" : "")}{(paidThrough is null ? "" : ", paid_through")})
            VALUES ('{id}', '{tenant}', '{signUp}', '{state}', now() - interval '3 days', {nextAt}, '{Guid.CreateVersion7()}'{(pinsPlan ? ", 'team', 1" : "")}{(paidThrough is { } through ? $", '{through:yyyy-MM-dd}'" : "")})
            """);
        return id;
    }

    private Task<string> StateAsync(Guid id) => ScalarAsync<string>($"SELECT state FROM control_plane.subscriptions WHERE id = '{id}'");

    private Task<Guid> StepAsync(Guid id) => ScalarAsync<Guid>($"SELECT step_id FROM control_plane.subscriptions WHERE id = '{id}'");

    private Task<string> TextAsync(string sql) => ScalarAsync<string>(sql);

    private Task<int> Execute(string sql) => ControlPlaneDatabase.ExecuteAsync(_dataSource, sql, TestContext.Current.CancellationToken);

    private Task<T> ScalarAsync<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    /// <summary>Stands in for the operator's identity provider: its discovery document and the key set of the key that signs the tokens.</summary>
    private sealed class IdentityProvider(RSA key) : HttpMessageHandler
    {
        public const string KeyId = "billing-key";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/realms/billing/.well-known/openid-configuration")
            {
                return Task.FromResult(Json(new { issuer = Authority, jwks_uri = $"{Authority}/protocol/openid-connect/certs" }));
            }

            if (path == "/realms/billing/protocol/openid-connect/certs")
            {
                var parameters = key.ExportParameters(false);
                return Task.FromResult(Json(new
                {
                    keys = new[]
                    {
                        new
                        {
                            kty = "RSA",
                            use = "sig",
                            alg = "RS256",
                            kid = KeyId,
                            n = Base64UrlEncoder.Encode(parameters.Modulus),
                            e = Base64UrlEncoder.Encode(parameters.Exponent),
                        },
                    },
                }));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
