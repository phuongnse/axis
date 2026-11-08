using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Axis.Core.Telemetry;
using Axis.Server.Http;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// The calls to <c>/mcp</c> count in the MCP partition by tenant and client id, against the development realm's Keycloak.
/// The limit is low and the allowance never refills during a test. Tenant <c>a</c> is served at <c>localhost</c>. The
/// database is unreachable, so a request that reaches it fails the test.
/// </summary>
public sealed class McpRateLimitTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string Host = "localhost";
    private const string Version = "2025-11-25";
    private const int McpLimit = 3;

    private Uri? _authority;
    private string? _applications;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_past_the_mcp_limit_is_refused_while_another_client_of_the_tenant_still_passes()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        var other = await RequestTokenAsync("author", KeycloakFixture.McpOtherClientId, KeycloakFixture.McpOtherClientSecret);

        for (var call = 0; call < McpLimit; call++)
        {
            using var allowed = await PostAsync(token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await PostAsync(token);
        using var otherClient = await PostAsync(other);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("Too many requests.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, otherClient.StatusCode);
    }

    [Fact]
    public async Task A_refused_mcp_call_counts_once_in_the_refusal_metric_with_the_mcp_partition()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        var meterFactory = _factory!.Services.GetRequiredService<IMeterFactory>();
        var partitions = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument.Name == AxisMetrics.RateLimitRefusals)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            Assert.Equal(1, measurement);
            foreach (var tag in tags)
            {
                Assert.Equal(AxisTelemetry.PartitionTag, tag.Key);
                partitions.Add((string)tag.Value!);
            }
        });
        listener.Start();

        for (var call = 0; call < McpLimit; call++)
        {
            using var allowed = await PostAsync(token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await PostAsync(token);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(["mcp"], partitions);
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        _applications = Directory.CreateTempSubdirectory("axis-mcp-limit-").FullName;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:Oidc:Authority", _authority.ToString());
            builder.UseSetting("Tenants:a:Oidc:ClientId", KeycloakFixture.ClientId);
            builder.UseSetting("Tenants:a:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", _applications);
            builder.ConfigureTestServices(services =>
            {
                // The allowance never refills during a test.
                services.AddSingleton(new RateLimitSettings(300, 1200, 1200, 60, McpLimit)
                {
                    ReplenishmentPeriod = TimeSpan.FromHours(1),
                });
            });
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
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

        if (_applications is not null)
        {
            Directory.Delete(_applications, recursive: true);
        }
    }

    private Task<string> RequestTokenAsync(string user, string clientId, string clientSecret) =>
        McpTestCalls.RequestTokenAsync(_authority!, user, clientId, clientSecret, CancellationToken);

    private Task<HttpResponseMessage> PostAsync(string bearer) =>
        McpTestCalls.PostAsync(
            _client ?? throw new InvalidOperationException("The test host is not started."),
            Host,
            bearer,
            McpTestCalls.Initialize(Version),
            session: null,
            version: null,
            origin: null,
            CancellationToken);
}
