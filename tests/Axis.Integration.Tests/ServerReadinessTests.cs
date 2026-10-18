using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Axis.Core.Telemetry;
using Axis.Core.Tenancy;
using Axis.Server.Health;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ServerReadinessTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string HostA = "a.example.test";
    private const string HostB = "b.example.test";
    private const string Healthy = """{"status":"Healthy"}""";
    private const string Degraded = """{"status":"Degraded"}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static string RecordsApp => Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordsApp");

    [Fact]
    public async Task Ready_is_unhealthy_while_activation_runs_and_healthy_after()
    {
        var (a, b) = await CreateDatabasesAsync();
        var result = new ReadinessResult();
        await using var factory = CreateFactory(a, b, RecordsApp, new LogCollector(), services =>
            // Lifecycle services start in registration order, so the probe runs before StartupActivation.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService>(provider =>
                new ReadinessProbe(provider.GetRequiredService<HealthCheckService>(), result))));
        using var client = factory.CreateClient();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        var (status, body) = await ReadyAsync(client);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Healthy, body);
    }

    [Fact]
    public async Task Ready_is_degraded_without_naming_a_tenant_when_one_tenant_database_is_down_and_the_other_tenant_works()
    {
        var (a, b) = await CreateDatabasesAsync();
        var logs = new LogCollector();
        await using var factory = CreateFactory(a, b, RecordsApp, logs);
        using var client = factory.CreateClient().SignedInAs("anna");
        var (healthyStatus, healthyBody) = await ReadyAsync(client);
        Assert.Equal(HttpStatusCode.OK, healthyStatus);
        Assert.Equal(Healthy, healthyBody);
        Assert.Equal(new Dictionary<string, long> { ["a"] = 1, ["b"] = 1 }, ReadGauge(factory));

        await StopAsync(a, b);
        try
        {
            var (status, body) = await ReadyAsync(client);

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(Degraded, body);
            Assert.Equal(new Dictionary<string, long> { ["a"] = 1, ["b"] = 0 }, ReadGauge(factory));
            Assert.Contains(logs.Entries, entry => entry.StartsWith("Warning: The database of tenant b does not answer.", StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Entries, entry => entry.Contains("tenant a does not answer", StringComparison.Ordinal));

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/apps/RecordsApp/entities/Department/records");
            request.Headers.Host = HostA;
            using var response = await client.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await AllowConnectionsAsync(a, b);
        }
    }

    [Fact]
    public async Task Reading_the_tenant_database_gauge_opens_no_database_connection()
    {
        var (a, b) = await CreateDatabasesAsync();
        await using var factory = CreateFactory(a, b, RecordsApp, new LogCollector(), services =>
        {
            services.AddSingleton(provider => new CountingTenantConnectionFactory(
                new TenantConnectionFactory(provider.GetRequiredService<TenantOptions>(), provider.GetRequiredService<ITenantContextAccessor>())));
            services.AddSingleton<ITenantConnectionFactory>(provider => provider.GetRequiredService<CountingTenantConnectionFactory>());
        });
        using var client = factory.CreateClient();
        await ReadyAsync(client);
        var opened = factory.Services.GetRequiredService<CountingTenantConnectionFactory>().Opened;

        var readings = new[] { ReadGauge(factory), ReadGauge(factory), ReadGauge(factory) };

        Assert.Equal(opened, factory.Services.GetRequiredService<CountingTenantConnectionFactory>().Opened);
        Assert.All(readings, reading => Assert.Equal(new Dictionary<string, long> { ["a"] = 1, ["b"] = 1 }, reading));
    }

    [Fact]
    public async Task Ready_is_degraded_within_five_seconds_when_a_tenant_database_never_replies()
    {
        var (a, b) = await CreateDatabasesAsync();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new List<TcpClient>();
        using var accepting = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var acceptLoop = Task.Run(
            async () =>
            {
                try
                {
                    while (true)
                    {
                        // The sockets stay open and unread, so the client waits for a reply that never comes.
                        accepted.Add(await listener.AcceptTcpClientAsync(accepting.Token));
                    }
                }
                catch (OperationCanceledException)
                {
                }
            },
            CancellationToken);
        try
        {
            await using var factory = CreateFactory(a, b, RecordsApp, new LogCollector(), services =>
            {
                services.AddSingleton(provider => new HangingTenantConnectionFactory(
                    new TenantConnectionFactory(provider.GetRequiredService<TenantOptions>(), provider.GetRequiredService<ITenantContextAccessor>()),
                    provider.GetRequiredService<ITenantContextAccessor>(),
                    port));
                services.AddSingleton<ITenantConnectionFactory>(provider => provider.GetRequiredService<HangingTenantConnectionFactory>());
            });
            using var client = factory.CreateClient();
            factory.Services.GetRequiredService<HangingTenantConnectionFactory>().Hang = true;

            var stopwatch = Stopwatch.StartNew();
            var (status, body) = await ReadyAsync(client);
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(Degraded, body);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"The readiness answer took {stopwatch.Elapsed}.");
        }
        finally
        {
            await accepting.CancelAsync();
            await acceptLoop;
            foreach (var client in accepted)
            {
                client.Dispose();
            }

            listener.Stop();
        }
    }

    [Fact]
    public async Task Ready_is_unhealthy_when_the_platform_database_is_unreachable_and_both_tenant_databases_are_up()
    {
        var (a, b) = await CreateDatabasesAsync();
        await using var factory = CreateFactory(a, b, folder: null, new LogCollector(), settings: new Dictionary<string, string>
        {
            // Port 1 on loopback refuses connections at once.
            ["ConnectionStrings:Platform"] = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2",
        });
        using var client = factory.CreateClient();

        var (status, body) = await ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("""{"status":"Unhealthy"}""", body);
    }

    private static async Task<(HttpStatusCode Status, string Body)> ReadyAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        request.Headers.Host = HostA;
        using var response = await client.SendAsync(request, CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    // Reads the gauge of this server only. Test servers share the process, so the listener
    // takes only instruments whose meter came from this server's meter factory.
    private static Dictionary<string, long> ReadGauge(WebApplicationFactory<Program> factory)
    {
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var values = new Dictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Scope == meterFactory && instrument.Name == AxisMetrics.TenantDatabaseAvailable)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == AxisTelemetry.TenantTag)
                {
                    values[(string)tag.Value!] = measurement;
                }
            }
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    // The tenant's database cannot be stopped, since every test class shares one PostgreSQL container.
    // Refusing connections and ending the open sessions looks the same to the server.
    private static async Task StopAsync(string a, string b)
    {
        var name = new NpgsqlConnectionStringBuilder(b).Database!;
        await PostgreSqlServer.AdminScalarAsync(a, $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS false""", CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(
            a, $"SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = '{name}'", CancellationToken);
    }

    private static async Task AllowConnectionsAsync(string a, string b)
    {
        var name = new NpgsqlConnectionStringBuilder(b).Database!;
        await PostgreSqlServer.AdminScalarAsync(a, $"""ALTER DATABASE "{name}" ALLOW_CONNECTIONS true""", CancellationToken.None);
    }

    private async Task<(string A, string B)> CreateDatabasesAsync() =>
        (await database.CreateDatabaseAsync("ready", CancellationToken), await database.CreateDatabaseAsync("ready", CancellationToken));

    private static WebApplicationFactory<Program> CreateFactory(
        string a,
        string b,
        string? folder,
        LogCollector logs,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(a));
            builder.UseSetting("Tenants:a:Hosts:0", HostA);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(a));
            builder.UseSetting("Tenants:a:SchemaConnectionString", a);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", PostgreSqlServer.RuntimeConnectionString(b));
            builder.UseSetting("Tenants:b:SchemaConnectionString", b);
            builder.UseTestOidc("a", "b");
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna Employee", "employee"));
            if (folder is not null)
            {
                builder.UseSetting("ActivateOnStartup:0", folder);
            }

            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });

    /// <summary>The readiness status of the server as <see cref="ReadinessProbe"/> saw it.</summary>
    private sealed class ReadinessResult
    {
        public HealthStatus? Status { get; set; }
    }

    /// <summary>Asks the readiness checks once, in <c>StartingAsync</c>, and keeps the status.</summary>
    private sealed class ReadinessProbe(HealthCheckService health, ReadinessResult result) : IHostedLifecycleService
    {
        public async Task StartingAsync(CancellationToken cancellationToken)
        {
            var report = await health.CheckHealthAsync(
                registration => registration.Tags.Contains(HealthEndpoints.ReadyTag), cancellationToken);
            result.Status = report.Status;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>
    /// Opens the connections of tenant <c>b</c> to a local port that never replies once <see cref="Hang"/> is
    /// set, and delegates every other connection. Startup needs a working database, so it starts unset.
    /// </summary>
    private sealed class HangingTenantConnectionFactory(
        TenantConnectionFactory inner,
        ITenantContextAccessor accessor,
        int port) : ITenantConnectionFactory, IDisposable
    {
        private volatile bool _hang;

        public bool Hang
        {
            get => _hang;
            set => _hang = value;
        }

        public async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (!_hang || accessor.Current?.TenantId != "b")
            {
                return await inner.OpenConnectionAsync(cancellationToken);
            }

            var connection = new NpgsqlConnection($"Host=127.0.0.1;Port={port};Database=x;Username=x;Password=x;Timeout=30;Pooling=false");
            try
            {
                await connection.OpenAsync(cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            return connection;
        }

        public void Dispose() => inner.Dispose();
    }

    /// <summary>Counts the connections opened through <paramref name="inner"/>.</summary>
    private sealed class CountingTenantConnectionFactory(TenantConnectionFactory inner) : ITenantConnectionFactory, IDisposable
    {
        private int _opened;

        public int Opened => Volatile.Read(ref _opened);

        public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);
            return inner.OpenConnectionAsync(cancellationToken);
        }

        public void Dispose() => inner.Dispose();
    }
}
