using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.ControlPlane.BillingEvents;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Subscriptions;
using Axis.ControlPlane.Telemetry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// The signed events to the operator's billing system on a real PostgreSQL, see <c>docs/reference/billing-api.md#events</c>. A stub receiver stands in for
/// the billing system, given through the client factory as the cell tests do, so the signature, the retries and the failure are checked without a TLS
/// server. Each test has a container of its own and waits for the hosted job runner with a deadline, or runs the job runner itself with no backoff.
/// </summary>
public sealed class BillingEventTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string Url = "https://billing.example/events";
    private const string FirstKey = "first-signing-key";
    private const string SecondSecret = "plain-second";
    private const string Subdomain = "acme-4f9c";

    private static readonly string _firstSecret = "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(FirstKey));
    private static readonly string _secrets = $"{_firstSecret} {SecondSecret}";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await ControlPlaneDatabase.MigrateAsync(_container.GetConnectionString(), TestContext.Current.CancellationToken);
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_confirmed_sign_up_delivers_one_signed_tenant_created_event_whose_body_holds_no_secret()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        using var factory = await FactoryAsync(receiver, billingEvents: true);
        using var client = factory.CreateClient();
        var token = await SeedPendingSignUpAsync();

        var confirm = await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation);

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await WaitAsync(() => Task.FromResult(!receiver.Requests.IsEmpty), cancellation);
        await WaitAsync(async () => await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent' AND state = 'succeeded'") == 1, cancellation);
        var request = Assert.Single(receiver.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Url, request.Uri);
        var body = JsonNode.Parse(request.Body)!.AsObject();
        var id = body["id"]!.GetValue<string>();
        Assert.Equal(id, request.Headers["webhook-id"]);
        Assert.True(Guid.TryParseExact(id, "D", out _));
        Assert.Equal("tenant.created", body["type"]!.GetValue<string>());
        Assert.Equal("v1", body["apiVersion"]!.GetValue<string>());
        var timestamp = DateTimeOffset.Parse(body["timestamp"]!.GetValue<string>(), CultureInfo.InvariantCulture);
        var data = body["data"]!.AsObject();
        Assert.Equal(Subdomain, data["tenantId"]!.GetValue<string>());
        Assert.Equal("vn", data["region"]!.GetValue<string>());
        Assert.Equal($"{Subdomain}.vn.example.com", data["host"]!.GetValue<string>());
        Assert.Equal("Acme Trading", data["businessName"]!.GetValue<string>());
        Assert.Equal("An Nguyen", data["administrator"]!["name"]!.GetValue<string>());
        Assert.Equal("an@acme-4f9c.example", data["administrator"]!["email"]!.GetValue<string>());
        Assert.Equal("vi", data["administrator"]!["language"]!.GetValue<string>());
        Assert.Equal(SubscriptionClock.TrialLength, DateTimeOffset.Parse(data["trialEndsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture) - timestamp);

        // The signature is recomputed here from the Standard Webhooks text, with each of the two secrets, and not with the code under test.
        var unixSeconds = request.Headers["webhook-timestamp"];
        var signed = Encoding.UTF8.GetBytes($"{id}.{unixSeconds}.{request.Body}");
        var expected = string.Join(
            ' ',
            new[] { Encoding.UTF8.GetBytes(FirstKey), Encoding.UTF8.GetBytes(SecondSecret) }
                .Select(key => "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, signed))));
        Assert.Equal(expected, request.Headers["webhook-signature"]);
        Assert.True(long.TryParse(unixSeconds, CultureInfo.InvariantCulture, out var sent));
        Assert.InRange(sent, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        foreach (var forbidden in new[] { token, _firstSecret, FirstKey, SecondSecret, Convert.ToBase64String(Encoding.UTF8.GetBytes(FirstKey)), "password" })
        {
            Assert.DoesNotContain(forbidden, request.Body, StringComparison.OrdinalIgnoreCase);
        }

        // The job's payload sits in the database, so it holds no personal data either.
        var payload = await Scalar<string>("SELECT payload::text FROM control_plane.jobs WHERE kind = 'sendBillingEvent'");
        foreach (var personal in new[] { "An Nguyen", "an@acme-4f9c.example", "Acme Trading" })
        {
            Assert.DoesNotContain(personal, payload, StringComparison.Ordinal);
        }

        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
    }

    [Fact]
    public async Task A_receiver_that_answers_500_gets_every_attempt_with_the_same_id_and_body_and_the_last_failure_is_counted()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var receiver = new ReceiverStub(HttpStatusCode.InternalServerError);
        var options = new ControlPlaneOptions { BillingEvents = new ControlPlaneBillingEventsOptions { Url = Url, Secret = _secrets } };
        var signUpId = await SeedSignUpAsync(state: "verified", token: null);
        await using (var connection = await _dataSource.OpenConnectionAsync(cancellation))
        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            await BillingEventQueue.EnqueueAsync(
                transaction,
                options,
                BillingEventQueue.TenantCreated,
                DateTimeOffset.UtcNow,
                new JsonObject { ["tenantId"] = Subdomain },
                signUpId,
                cancellation);
            await transaction.CommitAsync(cancellation);
        }

        var runnerOptions = new JobRunnerOptions { BaseDelay = TimeSpan.Zero };
        var handler = new SendBillingEventJobHandler(_dataSource, options, new StubHttpClientFactory(receiver), TimeProvider.System);
        var runner = new JobRunner(_dataSource, [handler], runnerOptions, new LoggerFactory().CreateLogger<JobRunner>());
        var measurements = new ConcurrentBag<(long Value, string? Kind)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name == ControlPlaneMetrics.JobsFailed)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            string? kind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == ControlPlaneMetrics.JobKindTag)
                {
                    kind = tag.Value as string;
                }
            }

            measurements.Add((value, kind));
        });
        listener.Start();

        for (var attempt = 1; attempt <= runnerOptions.MaxAttempts; attempt++)
        {
            Assert.True(await runner.RunOnceAsync(cancellation));
        }

        Assert.False(await runner.RunOnceAsync(cancellation));
        Assert.Equal(runnerOptions.MaxAttempts, receiver.Requests.Count);
        Assert.Single(receiver.Requests.Select(request => request.Headers["webhook-id"]).Distinct());
        Assert.Single(receiver.Requests.Select(request => request.Body).Distinct());
        Assert.Equal("failed", await Scalar<string>("SELECT state FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
        var error = await Scalar<string>("SELECT last_error FROM control_plane.jobs WHERE kind = 'sendBillingEvent'");
        Assert.Equal("System.Net.Http.HttpRequestException: The billing events receiver answered 500.", error);
        Assert.Equal((1L, "sendBillingEvent"), Assert.Single(measurements));
    }

    [Fact]
    public async Task Without_the_billing_events_address_a_confirm_queues_no_event_and_the_control_plane_answers_ready()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var receiver = new ReceiverStub(HttpStatusCode.OK);
        using var factory = await FactoryAsync(receiver, billingEvents: false);
        using var client = factory.CreateClient();
        var token = await SeedPendingSignUpAsync();

        var confirm = await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation);

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.subscriptions"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendBillingEvent'"));
        Assert.Empty(receiver.Requests);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready", cancellation)).StatusCode);
    }

    // The control plane without a directory, a cell or a DNS server, as the sign-up tests have it but with the mail relay that the ready check asks for.
    // The recurring jobs are removed and the runner polls fast, so a test waits only for the event job.
    private async Task<WebApplicationFactory<Program>> FactoryAsync(ReceiverStub receiver, bool billingEvents)
    {
        var host = await mailpit.SmtpHostAsync();
        var port = await mailpit.SmtpPortAsync();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "VN.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5");
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
            if (billingEvents)
            {
                builder.UseSetting("ControlPlane:BillingEvents:Url", Url);
                builder.UseSetting("ControlPlane:BillingEvents:Secret", _secrets);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<RecurringJob>();
                services.AddSingleton(new JobRunnerOptions { PollInterval = TimeSpan.FromMilliseconds(20) });
                services.AddHttpClient(SendBillingEventJobHandler.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => receiver);
            });
        });
    }

    // A pending sign-up with a token that the confirm route accepts. The token is returned.
    private async Task<string> SeedPendingSignUpAsync()
    {
        var token = Base64UrlToken(RandomNumberGenerator.GetBytes(32));
        await SeedSignUpAsync(state: "pending", token: token);
        return token;
    }

    private async Task<Guid> SeedSignUpAsync(string state, string? token)
    {
        var id = Guid.CreateVersion7();
        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO control_plane.sign_ups
                (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, token_hash, expires_at,
                 terms_version, dpa_version, privacy_version)
            VALUES (@id, 'An Nguyen', 'an@acme-4f9c.example', 'an@acme-4f9c.example', 'Acme Trading', @subdomain, 'vi', '', @state, @tokenHash,
                    now() + interval '1 hour', '2026-01', '2026-02', '2026-03')
            """);
        insert.Parameters.AddWithValue("id", id);
        insert.Parameters.AddWithValue("subdomain", Subdomain);
        insert.Parameters.AddWithValue("state", state);
        insert.Parameters.Add(new NpgsqlParameter("tokenHash", NpgsqlTypes.NpgsqlDbType.Bytea)
        {
            Value = token is null ? DBNull.Value : SHA256.HashData(System.Buffers.Text.Base64Url.DecodeFromChars(token)),
        });
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private static string Base64UrlToken(byte[] bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    private Task<T> Scalar<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_dataSource, sql, TestContext.Current.CancellationToken);

    private static async Task WaitAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 60 seconds.");
            await Task.Delay(50, cancellationToken);
        }
    }

    private sealed record ReceivedRequest(HttpMethod Method, string? Uri, IReadOnlyDictionary<string, string> Headers, string Body);

    // Stands in for the billing system's receiver: it records each request and answers the status it is given.
    private sealed class ReceiverStub(HttpStatusCode status) : HttpMessageHandler
    {
        public ConcurrentQueue<ReceivedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value), StringComparer.OrdinalIgnoreCase);
            Requests.Enqueue(new ReceivedRequest(request.Method, request.RequestUri?.ToString(), headers, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status);
        }
    }
}
