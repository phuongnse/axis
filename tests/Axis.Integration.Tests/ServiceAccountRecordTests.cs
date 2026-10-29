using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A service account calls the record API, reads data source rows, starts processes, inspects their instances and
/// resolves, retries and cancels them with the operator commands, with a client credentials token from the tenant's identity provider. Tenant
/// a at localhost serves the ServiceAccountApp fixture on a real database and expects the audience
/// <c>axis-services</c>. Tenant b at b.example.test uses Keycloak's master realm, so it has another issuer. Tenant c at
/// c.example.test uses the development realm but has no service audience. The database of b and c is unreachable, so
/// a request that reaches it fails the test.
/// </summary>
public sealed class ServiceAccountRecordTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostA = "localhost";
    private const string HostB = "b.example.test";
    private const string HostC = "c.example.test";
    private const string Reports = "/api/apps/ServiceAccountApp/entities/Report/records";
    private const string Restricted = "/api/apps/ServiceAccountApp/entities/Restricted/records";
    private const string ReportTitles = "/api/apps/ServiceAccountApp/data-sources/ReportTitles/rows";
    private const string ReportStatuses = "/api/apps/ServiceAccountApp/data-sources/ReportStatuses/rows";
    private const string Review = "/api/apps/ServiceAccountApp/processes/Review/instances";
    private const string Escalate = "/api/apps/ServiceAccountApp/processes/Escalate/instances";
    private const string Publish = "/api/apps/ServiceAccountApp/processes/Publish/instances";
    private const string SignInTitle = "Sign in to read or write this data.";
    private const string BearerTitle = "A valid bearer token is needed for this request.";

    private readonly PostgreSqlFixture _database = new();
    private Uri? _authority;
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private ReportTable? _reportTable;
    private Guid _releaseId;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    // The Publish process calls the Publishing connector, so the release needs its bindings.
    private static ActivationBindings PublishingBindings { get; } = new(
        "default",
        new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["PublishingUrl"] = new(ActivationBinding.UrlKind, new Uri("https://publishing.example.test")),
            ["PublishingToken"] = new(ActivationBinding.SecretKind, null),
        },
        AllowHttp: false);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_reporting_token_creates_reads_updates_and_deletes_a_record_and_reads_its_history_without_a_csrf_header()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);

        using var created = await SendAsync(HttpMethod.Post, Reports, token, """{ "values": { "title": "Q1", "status": "open" } }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("reporting-system", createdBody.RootElement.GetProperty("values").GetProperty("createdBy").GetString());

        using var read = await SendAsync(HttpMethod.Get, $"{Reports}/{id:D}", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var readBody = await ReadJsonAsync(read);
        Assert.Equal("Q1", readBody.RootElement.GetProperty("values").GetProperty("title").GetString());
        Assert.Equal("reporting-system", readBody.RootElement.GetProperty("values").GetProperty("createdBy").GetString());

        using var list = await SendAsync(HttpMethod.Get, Reports, token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = await ReadJsonAsync(list);
        Assert.Contains(id, listBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        using var updated = await SendAsync(HttpMethod.Patch, $"{Reports}/{id:D}", token, """{ "version": 1, "values": { "title": "Q1 final" } }""");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var history = await SendAsync(HttpMethod.Get, $"{Reports}/{id:D}/history", token);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using var historyBody = await ReadJsonAsync(history);
        var items = historyBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["record.updated", "record.created"], items.Select(item => item.GetProperty("action").GetString()));
        Assert.All(items, item =>
        {
            Assert.Equal("reporting-system", item.GetProperty("actor").GetString());
            Assert.Equal("reporting-system", item.GetProperty("actorName").GetString());
        });

        using var deleted = await SendAsync(HttpMethod.Delete, $"{Reports}/{id:D}", token);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var audits = await AuditRecordsAsync(id);
        Assert.Equal(["record.created", "record.updated", "record.deleted"], audits.Select(audit => audit.Action));
        Assert.All(audits, audit =>
        {
            Assert.Equal("reporting-system", audit.Actor);
            Assert.Equal("reporting-system", audit.ActorName);
        });
    }

    [Fact]
    public async Task A_service_account_has_no_grant_on_another_entity_and_its_record_filter_hides_rows()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);
        var visible = await InsertReportAsync("Visible", "open");
        var hidden = await InsertReportAsync("Hidden", "hidden");

        using var restrictedList = await SendAsync(HttpMethod.Get, Restricted, token);
        using var restrictedCreate = await SendAsync(HttpMethod.Post, Restricted, token, """{ "values": { "title": "No" } }""");
        using var list = await SendAsync(HttpMethod.Get, Reports, token);
        using var readHidden = await SendAsync(HttpMethod.Get, $"{Reports}/{hidden:D}", token);

        Assert.Equal(HttpStatusCode.Forbidden, restrictedList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, restrictedCreate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = await ReadJsonAsync(list);
        var ids = listBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(visible, ids);
        Assert.DoesNotContain(hidden, ids);
        Assert.Equal(HttpStatusCode.NotFound, readHidden.StatusCode);
    }

    [Fact]
    public async Task A_reporting_token_reads_data_source_rows_starts_a_process_and_inspects_its_instance_without_a_csrf_header()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);
        var report = await InsertReportAsync("Q2", "open");

        using var rows = await SendAsync(HttpMethod.Get, ReportTitles, token);
        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        using var rowsBody = await ReadJsonAsync(rows);
        Assert.Contains(report, rowsBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        using var started = await SendAsync(HttpMethod.Post, Review, token, $$"""{ "subjectId": "{{report:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var startedBody = await ReadJsonAsync(started);
        var instance = startedBody.RootElement.GetProperty("id").GetGuid();

        using var list = await SendAsync(HttpMethod.Get, Review, token);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = await ReadJsonAsync(list);
        Assert.Contains(instance, listBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));

        using var detail = await SendAsync(HttpMethod.Get, $"{Review}/{instance:D}", token);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = await ReadJsonAsync(detail);
        Assert.Equal(report, detailBody.RootElement.GetProperty("subjectId").GetGuid());

        var audit = Assert.Single(await ProcessStartAuditsAsync(report));
        Assert.Equal("reporting-system", audit.Actor);
        Assert.Equal("reporting-system", audit.ActorName);
    }

    [Fact]
    public async Task A_service_account_without_the_grant_gets_403_on_a_data_source_and_a_process_and_its_record_filter_hides_rows_and_instances()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);
        var visible = await InsertReportAsync("Visible", "open");

        using var statuses = await SendAsync(HttpMethod.Get, ReportStatuses, token);
        using var escalate = await SendAsync(HttpMethod.Post, Escalate, token, $$"""{ "subjectId": "{{visible:D}}" }""");
        using var escalated = await SendAsync(HttpMethod.Get, Escalate, token);
        using var escalatedOne = await SendAsync(HttpMethod.Get, $"{Escalate}/{Guid.NewGuid():D}", token);
        Assert.Equal(HttpStatusCode.Forbidden, statuses.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, escalated.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, escalatedOne.StatusCode);

        var first = await InsertReportAsync("First", "open");
        var second = await InsertReportAsync("Second", "open");
        var firstInstance = await StartReviewAsync(token, first);
        var secondInstance = await StartReviewAsync(token, second);
        await UpdateStatusAsync(second, "hidden");
        var hidden = await InsertReportAsync("Hidden", "hidden");

        using var rows = await SendAsync(HttpMethod.Get, ReportTitles, token);
        using var list = await SendAsync(HttpMethod.Get, Review, token);
        using var readSecond = await SendAsync(HttpMethod.Get, $"{Review}/{secondInstance:D}", token);
        Assert.Equal(HttpStatusCode.OK, rows.StatusCode);
        using var rowsBody = await ReadJsonAsync(rows);
        var rowIds = rowsBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(first, rowIds);
        Assert.DoesNotContain(second, rowIds);
        Assert.DoesNotContain(hidden, rowIds);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = await ReadJsonAsync(list);
        Assert.Equal(
            [firstInstance],
            listBody.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
        Assert.Equal(HttpStatusCode.NotFound, readSecond.StatusCode);
    }

    [Fact]
    public async Task A_service_token_is_ignored_on_the_task_routes_and_the_form_state_endpoint()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);

        // The CSRF header is sent so that a 401 comes from the session check, as the token does not exempt these routes.
        using var tasks = await SendAsync(HttpMethod.Get, "/api/apps/ServiceAccountApp/tasks", token, csrf: true);
        using var complete = await SendAsync(HttpMethod.Post, $"/api/apps/ServiceAccountApp/tasks/{Guid.NewGuid():D}/complete", token, "{}", csrf: true);
        using var formState = await SendAsync(HttpMethod.Post, "/api/apps/ServiceAccountApp/forms/Anything/state", token, "{}", csrf: true);

        foreach (var response in new[] { tasks, complete, formState })
        {
            var body = await response.Content.ReadAsStringAsync(CancellationToken);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {body}");
            using var problem = JsonDocument.Parse(body);
            Assert.Equal(SignInTitle, problem.RootElement.GetProperty("title").GetString());
            Assert.False(response.Headers.Contains("WWW-Authenticate"));
        }
    }

    [Theory]
    [InlineData("endpoint audience only", HostA)]
    [InlineData("another tenant", HostB)]
    [InlineData("no service audience", HostC)]
    public async Task An_unusable_token_is_a_401_with_a_bearer_challenge(string token, string host)
    {
        var bearer = token == "endpoint audience only"
            ? await RequestTokenAsync(KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret)
            : await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);

        using var response = await SendAsync(HttpMethod.Get, Reports, bearer, host: host);

        await AssertBearerChallengeAsync(response);
    }

    [Fact]
    public async Task A_garbled_token_is_a_401_with_a_bearer_challenge_on_the_data_source_and_process_routes()
    {
        using var rows = await SendAsync(HttpMethod.Get, ReportTitles, "not-a-token");
        using var start = await SendAsync(HttpMethod.Post, Review, "not-a-token", $$"""{ "subjectId": "{{Guid.NewGuid():D}}" }""");
        using var list = await SendAsync(HttpMethod.Get, Review, "not-a-token");
        using var one = await SendAsync(HttpMethod.Get, $"{Review}/{Guid.NewGuid():D}", "not-a-token");
        var operators = new List<HttpResponseMessage>();
        foreach (var path in OperatorPaths(Publish, Guid.NewGuid(), Guid.NewGuid()))
        {
            operators.Add(await SendAsync(HttpMethod.Post, path, "not-a-token", """{ "reason": "No", "outcome": "failed" }"""));
        }

        await AssertBearerChallengeAsync(rows);
        await AssertBearerChallengeAsync(start);
        await AssertBearerChallengeAsync(list);
        await AssertBearerChallengeAsync(one);
        foreach (var response in operators)
        {
            await AssertBearerChallengeAsync(response);
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_reporting_token_resolves_retries_and_cancels_instances_and_the_audit_names_it_without_a_csrf_header()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);
        var first = await InsertReportAsync("Resolved", "open");
        var second = await InsertReportAsync("Cancelled", "open");
        var firstInstance = await StartAsync(token, Publish, first);
        var secondInstance = await StartAsync(token, Publish, second);
        var call = await MakeCallUnknownAsync(firstInstance);

        using var resolved = await SendAsync(HttpMethod.Post, $"{Publish}/{firstInstance:D}/calls/{call:D}/resolve", token, """{ "reason": "Checked", "outcome": "failed" }""");
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        using var resolvedBody = await ReadJsonAsync(resolved);
        Assert.Equal("failed", resolvedBody.RootElement.GetProperty("state").GetString());

        using var retried = await SendAsync(HttpMethod.Post, $"{Publish}/{firstInstance:D}/retry", token, """{ "reason": "Again" }""");
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        using var retriedBody = await ReadJsonAsync(retried);
        Assert.Equal("running", retriedBody.RootElement.GetProperty("state").GetString());

        using var cancelled = await SendAsync(HttpMethod.Post, $"{Publish}/{secondInstance:D}/cancel", token, """{ "reason": "Not needed" }""");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        using var cancelledBody = await ReadJsonAsync(cancelled);
        Assert.Equal("cancelled", cancelledBody.RootElement.GetProperty("state").GetString());

        var firstAudits = await InstanceAuditsAsync(firstInstance);
        var secondAudits = await InstanceAuditsAsync(secondInstance);
        foreach (var action in new[] { "connector.callResolved", "process.failed", "process.retried" })
        {
            Assert.Contains(action, firstAudits.Select(audit => audit.Action));
        }

        Assert.Contains("process.cancelled", secondAudits.Select(audit => audit.Action));
        Assert.All(firstAudits.Concat(secondAudits).Where(audit => audit.Action != "process.started"), audit =>
        {
            Assert.Equal("reporting-system", audit.Actor);
            Assert.Equal("reporting-system", audit.ActorName);
        });

        foreach (var (report, actions) in new[]
        {
            (first, new[] { "connector.callResolved", "process.failed", "process.retried" }),
            (second, new[] { "process.cancelled" }),
        })
        {
            using var history = await SendAsync(HttpMethod.Get, $"{Reports}/{report:D}/history", token);
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            using var historyBody = await ReadJsonAsync(history);
            var items = historyBody.RootElement.GetProperty("items").EnumerateArray().ToList();
            foreach (var action in actions)
            {
                var item = Assert.Single(items, candidate => candidate.GetProperty("action").GetString() == action);
                Assert.Equal("reporting-system", item.GetProperty("actor").GetString());
                Assert.Equal("reporting-system", item.GetProperty("actorName").GetString());
            }
        }
    }

    [Fact]
    public async Task A_service_account_without_the_operator_grants_gets_403_and_a_hidden_subject_gets_404_on_the_operator_commands()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);

        // Review grants start and inspect only, and the grant check runs before the instance lookup.
        foreach (var path in OperatorPaths(Review, Guid.NewGuid(), Guid.NewGuid()))
        {
            using var denied = await SendAsync(HttpMethod.Post, path, token, """{ "reason": "No", "outcome": "failed" }""");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var report = await InsertReportAsync("Soon hidden", "open");
        var instance = await StartAsync(token, Publish, report);
        await UpdateStatusAsync(report, "hidden");
        foreach (var path in OperatorPaths(Publish, instance, Guid.NewGuid()))
        {
            using var hidden = await SendAsync(HttpMethod.Post, path, token, OperatorBody(path));
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }
    }

    [Fact]
    public async Task A_token_of_the_wrong_audience_is_a_401_with_a_bearer_challenge_on_the_operator_commands()
    {
        var token = await RequestTokenAsync(KeycloakFixture.PurchasingClientId, KeycloakFixture.PurchasingClientSecret);

        foreach (var path in OperatorPaths(Publish, Guid.NewGuid(), Guid.NewGuid()))
        {
            using var response = await SendAsync(HttpMethod.Post, path, token, """{ "reason": "No", "outcome": "failed" }""");
            await AssertBearerChallengeAsync(response);
        }
    }

    [Fact]
    public async Task A_token_with_a_valid_cookie_is_judged_by_the_token_and_the_cookie_is_not_renewed()
    {
        var token = await RequestTokenAsync(KeycloakFixture.ReportingClientId, KeycloakFixture.ReportingClientSecret);
        var cookie = await SignInAnnaAsync();

        // The requester may read Restricted, the reporting system may not, so a 403 shows that the token judged the request.
        using var byToken = await SendAsync(HttpMethod.Get, Restricted, token, cookie: cookie);
        using var garbled = await SendAsync(HttpMethod.Get, Restricted, "not-a-token", cookie: cookie);
        using var unsafeByCookie = await SendAsync(HttpMethod.Post, Restricted, bearer: null, """{ "values": { "title": "No" } }""", cookie: cookie);
        using var safeByCookie = await SendAsync(HttpMethod.Get, Restricted, bearer: null, cookie: cookie);

        Assert.Equal(HttpStatusCode.Forbidden, byToken.StatusCode);
        Assert.False(byToken.Headers.Contains("Set-Cookie"));
        await AssertBearerChallengeAsync(garbled);
        Assert.False(garbled.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Forbidden, unsafeByCookie.StatusCode);
        using var csrf = JsonDocument.Parse(await unsafeByCookie.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("The request must carry the X-Axis-Csrf header.", csrf.RootElement.GetProperty("title").GetString());
        // For contrast, a request without a token reads the session and renews it.
        Assert.Equal(HttpStatusCode.OK, safeByCookie.StatusCode);
        Assert.True(safeByCookie.Headers.Contains("Set-Cookie"));
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        await _database.InitializeAsync();
        _connectionString = _database.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync(CancellationToken);
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync(CancellationToken);
            await data.Database.MigrateAsync(CancellationToken);
            await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
            {
                // A delete looks for the process instances of the record.
                await processes.Database.MigrateAsync(CancellationToken);
            }

            await PostgreSqlServer.GrantRuntimeRoleAsync(_connectionString, CancellationToken);

            var compiled = await ReleaseCompiler.CompileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ServiceAccountApp"), configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, PublishingBindings, OpenInstanceCounter.Instance);
            Assert.Empty(activated.Diagnostics);
            _releaseId = compiled.Release!.Id;
            Assert.NotNull(compiled.Model);
            Assert.True(compiled.Model.TryGetEntity("Report", out var report));
            _reportTable = new ReportTable(
                EntityNaming.QualifiedTable(EntityNaming.Table(report.Id)),
                EntityNaming.Quote(EntityNaming.IdColumn),
                EntityNaming.Quote(EntityNaming.Column("title")),
                EntityNaming.Quote(EntityNaming.Column("status")));
        }

        var authority = _authority.ToString();
        var masterRealm = new Uri(_authority, "master").ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            UseTenant(builder, "a", HostA, authority, PostgreSqlServer.RuntimeConnectionString(_connectionString), _connectionString, endpointAudience: "axis-endpoints", serviceAudience: "axis-services");
            UseTenant(builder, "b", HostB, masterRealm, UnreachableDatabase, UnreachableDatabase, endpointAudience: "axis-endpoints", serviceAudience: "axis-services");
            UseTenant(builder, "c", HostC, authority, UnreachableDatabase, UnreachableDatabase, endpointAudience: "axis-endpoints", serviceAudience: null);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new SessionStartupFilter()));
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    private static void UseTenant(
        IWebHostBuilder builder,
        string tenant,
        string host,
        string authority,
        string connectionString,
        string schemaConnectionString,
        string endpointAudience,
        string? serviceAudience)
    {
        builder.UseSetting($"Tenants:{tenant}:Hosts:0", host);
        builder.UseSetting($"Tenants:{tenant}:ConnectionString", connectionString);
        builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", schemaConnectionString);
        builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", authority);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
        builder.UseSetting($"Tenants:{tenant}:Oidc:EndpointAudience", endpointAudience);
        if (serviceAudience is not null)
        {
            builder.UseSetting($"Tenants:{tenant}:Oidc:ServiceAudience", serviceAudience);
        }
    }

    private async Task<string> RequestTokenAsync(string clientId, string clientSecret)
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });
        using var response = await client.PostAsync(new Uri($"{_authority}/protocol/openid-connect/token"), form, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> SignInAnnaAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/test/session", bearer: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value[..value.IndexOf(';', StringComparison.Ordinal)])
            .Single(value => value.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal));
        return cookie[(Session.Scheme.Length + 1)..];
    }

    /// <summary>Sends a request with the bearer token and session cookie given, and the CSRF header only when asked.</summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? bearer,
        string? body = null,
        string host = HostA,
        string? cookie = null,
        bool csrf = false)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        if (csrf)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await Client.SendAsync(request, CancellationToken);
    }

    private async Task<Guid> InsertReportAsync(string title, string status)
    {
        var table = _reportTable ?? throw new InvalidOperationException("The test host is not started.");
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
            $"INSERT INTO {table.Name} ({table.Id}, {table.Title}, {table.Status}) VALUES (@id, @title, @status)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task UpdateStatusAsync(Guid id, string status)
    {
        var table = _reportTable ?? throw new InvalidOperationException("The test host is not started.");
        await using var command = DataSource.CreateCommand($"UPDATE {table.Name} SET {table.Status} = @status WHERE {table.Id} = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<Guid> StartReviewAsync(string token, Guid report)
    {
        using var started = await SendAsync(HttpMethod.Post, Review, token, $$"""{ "subjectId": "{{report:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var body = await ReadJsonAsync(started);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> StartAsync(string token, string route, Guid report)
    {
        using var started = await SendAsync(HttpMethod.Post, route, token, $$"""{ "subjectId": "{{report:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        using var body = await ReadJsonAsync(started);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // The test host runs no worker, so the instance stays running on its connector step. This sets the state that a
    // worker leaves when a call may have reached the other system.
    private async Task<Guid> MakeCallUnknownAsync(Guid instance)
    {
        var call = Guid.CreateVersion7();
        await using var batch = DataSource.CreateBatch();
        batch.BatchCommands.Add(new NpgsqlBatchCommand("UPDATE axis.process_instances SET state = 'waiting' WHERE id = @instance")
        {
            Parameters = { new NpgsqlParameter("instance", instance) },
        });
        batch.BatchCommands.Add(new NpgsqlBatchCommand("DELETE FROM axis.process_work_items WHERE process_instance_id = @instance")
        {
            Parameters = { new NpgsqlParameter("instance", instance) },
        });
        batch.BatchCommands.Add(new NpgsqlBatchCommand(
            """
            INSERT INTO axis.connector_calls (id, process_instance_id, step, staged_revision, release_id, connector, operation, state, attempts)
            VALUES (@id, @instance, 'send', 1, @release, 'Publishing', 'publish', 'unknown', 0)
            """)
        {
            Parameters = { new NpgsqlParameter("id", call), new NpgsqlParameter("instance", instance), new NpgsqlParameter("release", _releaseId) },
        });
        await batch.ExecuteNonQueryAsync(CancellationToken);
        return call;
    }

    private static string OperatorBody(string path) =>
        path.EndsWith("/resolve", StringComparison.Ordinal) ? """{ "reason": "No", "outcome": "failed" }""" : """{ "reason": "No" }""";

    private static string[] OperatorPaths(string route, Guid instance, Guid call) =>
        [$"{route}/{instance:D}/cancel", $"{route}/{instance:D}/retry", $"{route}/{instance:D}/calls/{call:D}/resolve"];

    private async Task<IReadOnlyList<(string Action, string Actor, string? ActorName)>> InstanceAuditsAsync(Guid instance)
    {
        var records = new List<(string, string, string?)>();
        await using var command = DataSource.CreateCommand(
            "SELECT action, actor, actor_name FROM axis.audit_records WHERE process_instance_id = @instance ORDER BY occurred_at, id");
        command.Parameters.AddWithValue("instance", instance);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return records;
    }

    private async Task<IReadOnlyList<(string Actor, string? ActorName)>> ProcessStartAuditsAsync(Guid recordId)
    {
        var records = new List<(string, string?)>();
        await using var command = DataSource.CreateCommand(
            "SELECT actor, actor_name FROM axis.audit_records WHERE action = 'process.started' AND record_id = @record ORDER BY occurred_at, id");
        command.Parameters.AddWithValue("record", recordId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return records;
    }

    private async Task<IReadOnlyList<(string Action, string Actor, string? ActorName)>> AuditRecordsAsync(Guid recordId)
    {
        var records = new List<(string, string, string?)>();
        await using var command = DataSource.CreateCommand(
            "SELECT action, actor, actor_name FROM axis.audit_records WHERE record_id = @record ORDER BY occurred_at, id");
        command.Parameters.AddWithValue("record", recordId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            records.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return records;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

    private static async Task AssertBearerChallengeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{response.StatusCode}: {body}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(BearerTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    private sealed record ReportTable(string Name, string Id, string Title, string Status);

    /// <summary>Answers <c>GET /test/session</c> by signing requester, an employee, in to tenant a.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("a", "requester", "Requester User", ["employee"]));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }
}
