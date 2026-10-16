using System.Globalization;
using System.Net;
using System.Text.Json;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Processes.Work;
using Axis.PurchasingStub;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, served by the real server on host <c>a.example.test</c>. The
/// server migrates it and activates the purchase request sample through <c>ActivateOnStartup</c>,
/// so the sample's department seeds are applied as well. The test authentication scheme signs in
/// <c>anna</c> and <c>lan</c>, two employees, <c>chau</c>, the Operations manager, <c>binh</c>, the
/// Engineering manager, and <c>dung</c> and <c>hoa</c>, two finance reviewers. The fixture also holds the worker's services for the tenant, with the host never
/// started, so a test runs the work items itself. A test can instead start real worker hosts with
/// <see cref="StartWorkerAsync"/>, and stop them to stand for a worker that is down.
/// The purchasing system stub runs in process over <c>http</c>. The workers run in Development, the
/// only environment that lets a connector call an <c>http</c> URL, and their purchasing bindings
/// point at the stub. The server runs as Testing, so it would not load the development settings,
/// and its bindings hold an <c>https</c> placeholder: it only checks them when it activates the
/// sample, and never calls the stub.
/// </summary>
public sealed class SampleAppFixture : IAsyncLifetime
{
    public const string Tenant = "a";

    public const string Host = "a.example.test";

    /// <summary>The id of the seeded Operations department, whose manager is <c>chau</c>.</summary>
    public static readonly Guid Operations = Guid.Parse("2cec287b-4f8a-4ac4-a38c-08214a1846ae");

    /// <summary>The id of the seeded Engineering department, whose manager is <c>binh</c>.</summary>
    public static readonly Guid Engineering = Guid.Parse("15d5bba1-eaf1-431b-9001-e1e3ade50979");

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private const string App = "/api/apps/PurchaseRequests";

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private const string Token = "sample-test-token";

    private readonly PostgreSqlFixture _database = new();
    private WebApplication? _purchasing;
    private string? _purchasingUrl;
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ServiceProvider? _services;

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    /// <summary>The address of the purchasing system stub, such as <c>http://127.0.0.1:41234</c>.</summary>
    public string PurchasingUrl => _purchasingUrl ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        _purchasing = PurchasingStubApp.Create(["--urls", "http://127.0.0.1:0"]);
        await _purchasing.StartAsync(CancellationToken);
        _purchasingUrl = _purchasing.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();

        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync("sample_a");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", _connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Kind", "url");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Value", "https://purchasing.example.test");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Kind", "secret");
            builder.UseSetting($"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Value", Token);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("ActivateOnStartup:0", _appFolder);
            builder.UseTestAuthentication(
                new TestIdentity("anna", "Anna Employee", "employee"),
                new TestIdentity("lan", "Lan Employee", "employee"),
                new TestIdentity("chau", "Chau Operations Manager", "department-manager"),
                new TestIdentity("binh", "Binh Engineering Manager", "department-manager"),
                new TestIdentity("dung", "Dung Finance Reviewer", "finance-reviewer"),
                new TestIdentity("hoa", "Hoa Finance Reviewer", "finance-reviewer"));
        });

        // Creating a client starts the server, which migrates the database and activates the sample first.
        _factory.CreateClient().Dispose();

        var settings = new ConfigurationBuilder().AddInMemoryCollection(WorkerTenantSettings()).Build();
        _services = new ServiceCollection().AddLogging().AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Development", ContentRootPath = AppContext.BaseDirectory }).BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();

        if (_purchasing is not null)
        {
            await _purchasing.DisposeAsync();
        }
    }

    /// <summary>Claims and runs the tenant's due work items until none is left, and returns how many it ran.</summary>
    public async Task<int> RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var runner = (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        var ran = 0;
        while (await runner.RunNextAsync(Tenant, _lease, cancellationToken))
        {
            ran++;
        }

        return ran;
    }

    /// <summary>
    /// Starts a worker host for the tenant, with a two second lease, that logs to <paramref name="logs"/>.
    /// The caller stops and disposes it.
    /// </summary>
    internal async Task<IHost> StartWorkerAsync(LogCollector logs)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" });

        // In Development the host would also read appsettings.Development.json and user secrets,
        // which could add a default tenant this fixture cannot reach.
        builder.Configuration.Sources.Clear();
        var settings = WorkerTenantSettings();
        settings["Worker:PollInterval"] = "00:00:00.05";
        settings["Worker:LeaseDuration"] = "00:00:02";
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        var host = builder.Build();
        await host.StartAsync(CancellationToken);
        return host;
    }

    /// <summary>The worker's settings for the tenant, with its purchasing bindings pointing at the stub.</summary>
    private Dictionary<string, string?> WorkerTenantSettings() => new()
    {
        ["Extensions:Path"] = SamplePackages.Path,
        [$"Tenants:{Tenant}:Hosts:0"] = Host,
        [$"Tenants:{Tenant}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(ConnectionString),
        [$"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Kind"] = "url",
        [$"Tenants:{Tenant}:Bindings:PurchasingSystemUrl:Value"] = PurchasingUrl,
        [$"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Kind"] = "secret",
        [$"Tenants:{Tenant}:Bindings:PurchasingSystemToken:Value"] = Token,
    };

    /// <summary>Creates a draft Operations request with one line item of <paramref name="unitPrice"/>, and returns its id.</summary>
    public static Task<Guid> CreateRequestAsync(HttpClient client, decimal unitPrice) =>
        CreateRequestAsync(client, unitPrice, Operations);

    /// <summary>
    /// Creates a draft request in <paramref name="department"/> with one line item of <paramref name="unitPrice"/>,
    /// and returns its id.
    /// </summary>
    public static async Task<Guid> CreateRequestAsync(HttpClient client, decimal unitPrice, Guid department)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            $"{App}/entities/PurchaseRequest/records",
            $$"""
            {
              "values": {
                "title": "Request {{Guid.NewGuid():N}}",
                "status": "draft",
                "department": "{{department:D}}",
                "lineItems": [{ "description": "Desk", "quantity": 1, "unitPrice": {{unitPrice.ToString(CultureInfo.InvariantCulture)}} }]
              }
            }
            """);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    /// <summary>Starts the approval process for the request <paramref name="subjectId"/>, and returns the instance id.</summary>
    public static async Task<Guid> StartAsync(HttpClient client, Guid subjectId)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            $"{App}/processes/PurchaseRequestApproval/instances",
            $$"""{ "subjectId": "{{subjectId:D}}" }""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIdAsync(response);
    }

    /// <summary>Completes the task <paramref name="taskId"/> with the JSON <paramref name="body"/>. The caller disposes the response.</summary>
    public static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid taskId, string body) =>
        SendAsync(client, HttpMethod.Post, $"{App}/tasks/{taskId:D}/complete", body);

    /// <summary>The request's number and purchase order number, read through the record API.</summary>
    public static async Task<(string Number, string? PurchaseOrderNumber)> NumbersAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/entities/PurchaseRequest/records/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        return (
            values.GetProperty("number").GetString()!,
            values.TryGetProperty("purchaseOrderNumber", out var order) ? order.GetString() : null);
    }

    /// <summary>The order numbers that the purchasing system stub holds for the request <paramref name="requestNumber"/>.</summary>
    public async Task<IReadOnlyList<string?>> OrdersAsync(string requestNumber)
    {
        using var client = new HttpClient { BaseAddress = new Uri(PurchasingUrl) };
        using var body = JsonDocument.Parse(await client.GetStringAsync("/purchase-orders", CancellationToken));
        return [.. body.RootElement.EnumerateArray()
            .Where(order => order.GetProperty("requestNumber").GetString() == requestNumber)
            .Select(order => order.GetProperty("orderNumber").GetString())];
    }

    /// <summary>The connection string of the tenant database's owner, <see cref="PostgreSqlServer.SchemaRole"/>.</summary>
    public string ConnectionString => _connectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>
    /// Runs <paramref name="sql"/> on the tenant database, a <c>SELECT count(*)</c>, as the PostgreSQL superuser,
    /// which is the only role that sees or ends the sessions of other roles.
    /// </summary>
    public async Task<long> AdminCountAsync(string sql) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(ConnectionString, sql, CancellationToken))!;

    /// <summary>Runs <paramref name="sql"/> on the tenant database, a <c>SELECT count(*)</c>, with the named parameters.</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        (long)(await ScalarAsync(sql, parameters))!;

    /// <summary>Runs <paramref name="sql"/> on the tenant database with the named parameters, and returns the first column of the first row.</summary>
    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = (_dataSource ?? throw new InvalidOperationException("The fixture is not initialized.")).CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync(CancellationToken);
        return result is DBNull ? null : result;
    }

    /// <summary>Opens a new connection to the tenant database. The caller disposes it.</summary>
    public ValueTask<NpgsqlConnection> OpenConnectionAsync() =>
        (_dataSource ?? throw new InvalidOperationException("The fixture is not initialized.")).OpenConnectionAsync(CancellationToken);

    /// <summary>Waits until <paramref name="condition"/> holds, for at most 30 seconds.</summary>
    public static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold within 30 seconds.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
        }
    }

    /// <summary>The id of the sample's active release in the tenant.</summary>
    public async Task<Guid> ActiveReleaseIdAsync(CancellationToken cancellationToken)
    {
        await using var configuration = new ConfigurationDbContext(
            new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);
        var active = await new ActiveReleaseStore(configuration).FindByNameAsync("PurchaseRequests", cancellationToken);
        Assert.NotNull(active);
        return active.ReleaseId;
    }

    /// <summary>The id, state and release of each process instance of the record <paramref name="subjectId"/>, oldest first.</summary>
    public async Task<IReadOnlyList<InstanceRow>> InstancesAsync(Guid subjectId)
    {
        await using var command = (_dataSource ?? throw new InvalidOperationException("The fixture is not initialized.")).CreateCommand(
            "SELECT id, state, release_id FROM axis.process_instances WHERE subject_id = @subject ORDER BY created_at, id");
        command.Parameters.AddWithValue("subject", subjectId);
        var rows = new List<InstanceRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new InstanceRow(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2)));
        }

        return rows;
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string body)
    {
        using var request = RecordApiFixture.Request(method, path, Host, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}

/// <summary>A process instance's id, state and the release it is pinned to.</summary>
public sealed record InstanceRow(Guid Id, string State, Guid ReleaseId);
