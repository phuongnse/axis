using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Cells;
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
/// The billing API's paid-through call on a real PostgreSQL, see <c>docs/reference/billing-api.md</c>. The billing client signs in with a token that a stub
/// identity provider's key signs, and the control plane checks it as it checks the operator's real one. The moves then run end to end: the call, the change
/// of state, and the jobs that write the plan's quotas to a cell that <see cref="CellStub"/> stands in for, and resume the tenant. Each test class instance has
/// a container of its own, with the region <c>vn</c> and the cell <c>vn-1</c>. Every tenant holds a marker of ten digits.
/// </summary>
public sealed class BillingApiTests : IAsyncLifetime
{
    private const string Authority = "https://idp.test/realms/billing";
    private const string Audience = "axis-control-plane";
    private const string BillingClient = "billing-client";
    private const string Tenant = "bill-4172903856";
    private const string OtherTenant = "bill-5938162047";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly PlanCatalogue _catalogue = BillingSeed.Catalogue();
    private readonly CellStub _cell = new();
    private readonly RSA _key = RSA.Create(2048);
    private readonly DateOnly _paidThrough = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
    private NpgsqlDataSource _dataSource = null!;
    private ILoggerFactory _loggers = null!;
    private CellClients _clients = null!;
    private JobRunner _runner = null!;
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

        var controlPlane = new ControlPlaneOptions
        {
            TenantDomain = "vn.example.com",
            PublicUrl = "https://account.vn.example.com",
            MailFrom = "no-reply@vn.example.com",
        };
        controlPlane.Cells.Add(new CellOptions { Id = "vn-1", InternalUrl = "https://vn-1.test", TokenUrl = "https://vn-1.test/token", ClientId = "axis-controlplane" });
        controlPlane.CellSecrets["vn-1"] = "cell-secret";
        _clients = new CellClients(controlPlane, new StubHttpClientFactory(_cell), new CellClientOptions(), TimeProvider.System);
        _loggers = LoggerFactory.Create(_ => { });
        var quotas = new SubscriptionQuotas(_dataSource, _dataSource, _clients, _catalogue, TimeProvider.System, _loggers.CreateLogger<SubscriptionQuotas>());
        var resume = new SubscriptionResume(_dataSource, _dataSource, _clients, _loggers.CreateLogger<SubscriptionResume>());
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
            $"{_paidThrough.AddDays(1):yyyy-MM-dd}T00:00|",
            await TextAsync($"SELECT to_char(next_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI') || '|' || coalesce(period_ends_at::text, '') FROM control_plane.subscriptions WHERE id = '{id}'"));
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

    private async Task AssertUnchangedAsync(Guid id, string state)
    {
        Assert.Equal(state, await StateAsync(id));
        Assert.Equal(0L, await ScalarAsync<long>($"SELECT count(*) FROM control_plane.subscriptions WHERE id = '{id}' AND paid_through IS NOT NULL"));
        Assert.Empty(await AuditAsync(id.ToString()));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.jobs"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT count(*) FROM control_plane.billing_requests"));
    }

    private async Task<HttpResponseMessage> PutAsync(string tenant, string? key, string body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/billing/v1/tenants/{tenant}/subscription/paid-through")
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

    // A subscription of a verified sign-up that pins version 1 of the team plan, or none. A new trial pins no plan.
    private async Task<Guid> SeedAsync(string tenant, string state, bool pinsPlan = true)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await Execute($"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """);
        await Execute($"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, next_at, step_id{(pinsPlan ? ", plan_code, plan_version" : "")})
            VALUES ('{id}', '{tenant}', '{signUp}', '{state}', now() - interval '3 days', now(), '{Guid.CreateVersion7()}'{(pinsPlan ? ", 'team', 1" : "")})
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
