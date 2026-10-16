using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the EndpointApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The server's tenant pool holds one connection, so a request that keeps a connection blocks every
/// other request. The test endpoint caller scheme signs in <c>importer-system</c> and
/// <c>other-importer</c>, which hold <c>importer</c>, and <c>reader-system</c>, which holds
/// <c>reader</c>. The test authentication scheme signs in <c>ivy</c>, who holds <c>importer</c> and
/// completes tasks. The fixture also holds the worker's services, with the host never started, so a
/// test runs the instances' steps itself. The worker's connections are not limited.
/// </summary>
public sealed class EndpointOutputFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private const string Tenant = "a";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EndpointApp");

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ServiceProvider? _services;
    private ApplicationModel? _model;

    /// <summary>The compiled model of the EndpointApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with no caller and no user. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync("endpoint_output_a");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
        }

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            var compiled = await ReleaseCompiler.CompileAsync(_appFolder, configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(Tenant), OpenInstanceCounter.Instance);
            Assert.Empty(activated.Diagnostics);
            _model = compiled.Model!;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString) + ";Maximum Pool Size=1");
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseTestAuthentication(new TestIdentity("ivy", "Ivy Importer", "importer"));
            builder.UseTestEndpointCallers(
                new TestIdentity("importer-system", "Importer System", "importer"),
                new TestIdentity("other-importer", "Other Importer", "importer"),
                new TestIdentity("reader-system", "Reader System", "reader"));
        });

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenants:a:Hosts:0"] = Host,
                ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(_connectionString),
            })
            .Build();
        _services = new ServiceCollection().AddLogging().AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" }).BuildServiceProvider();
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

    /// <summary>Claims and runs the due work items of the tenant until none is left.</summary>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var runner = (_services ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync(Tenant, _lease, cancellationToken))
        {
        }
    }

    /// <summary>The id of the instance whose subject is the note titled <paramref name="title"/>, or null when there is none yet.</summary>
    public async Task<Guid?> FindInstanceAsync(string title)
    {
        Assert.True(Model.TryGetEntity("Note", out var note));
        await using var command = DataSource.CreateCommand(
            $"""
            SELECT i.id FROM axis.process_instances i
            JOIN {EntityNaming.QualifiedTable(EntityNaming.Table(note.Id))} n ON n.{EntityNaming.Quote(EntityNaming.IdColumn)} = i.subject_id
            WHERE n.{EntityNaming.Quote(EntityNaming.Column("title"))} = @title
            """);
        command.Parameters.AddWithValue("title", title);
        return await command.ExecuteScalarAsync() is Guid id ? id : null;
    }

    /// <summary>The id of the one task of the instance <paramref name="instanceId"/>.</summary>
    public async Task<Guid> TaskIdAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("The instance has no task."));
    }

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
