using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, served by the real server on host <c>a.example.test</c>. The
/// server migrates it and activates the purchase request sample through <c>ActivateOnStartup</c>,
/// so the sample's department seeds are applied as well. The server knows the test users
/// <c>anna</c>, an employee, and <c>chau</c>, the Operations manager. The fixture also holds the
/// worker's services for the tenant, with the host never started, so a test runs the work items itself.
/// </summary>
public sealed class SampleAppFixture : IAsyncLifetime
{
    public const string Tenant = "a";

    public const string Host = "a.example.test";

    /// <summary>The id of the seeded Operations department, whose manager is <c>chau</c>.</summary>
    public static readonly Guid Operations = Guid.Parse("2cec287b-4f8a-4ac4-a38c-08214a1846ae");

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ServiceProvider? _services;

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using (var admin = NpgsqlDataSource.Create(_database.ConnectionString))
        await using (var create = admin.CreateCommand("""CREATE DATABASE "sample_a" """))
        {
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "sample_a" }.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", _connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", _connectionString);
            builder.UseSetting("ActivateOnStartup:0", _appFolder);
            builder.UseSetting("TestUsers:0:Id", "anna");
            builder.UseSetting("TestUsers:0:DisplayName", "Anna Employee");
            builder.UseSetting("TestUsers:0:Roles:0", "employee");
            builder.UseSetting("TestUsers:1:Id", "chau");
            builder.UseSetting("TestUsers:1:DisplayName", "Chau Operations Manager");
            builder.UseSetting("TestUsers:1:Roles:0", "department-manager");
        });

        // Creating a client starts the server, which migrates the database and activates the sample first.
        _factory.CreateClient().Dispose();

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{Tenant}:Hosts:0"] = Host,
                [$"Tenants:{Tenant}:ConnectionString"] = _connectionString,
            })
            .Build();
        _services = new ServiceCollection().AddLogging().AddAxisWorker(settings).BuildServiceProvider();
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
    }

    /// <summary>Claims and runs the tenant's due work items until none is left.</summary>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var runner = (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync(Tenant, _lease, cancellationToken))
        {
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
}

/// <summary>A process instance's id, state and the release it is pinned to.</summary>
public sealed record InstanceRow(Guid Id, string State, Guid ReleaseId);
