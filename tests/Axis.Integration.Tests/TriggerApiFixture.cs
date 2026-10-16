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
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the TriggerApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The test authentication scheme signs in <c>cara</c>, who holds <c>clerk</c>, and the test
/// endpoint caller scheme signs in <c>ticket-system</c>, who holds <c>importer</c>. A worker exists,
/// but runs only when a test calls <see cref="RunUntilIdleAsync"/>, so a started instance stays
/// <c>running</c> until then. The log entries of the server and the worker go to <see cref="Logs"/>.
/// </summary>
public sealed class TriggerApiFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TriggerApp");

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ServiceProvider? _worker;
    private ApplicationModel? _model;

    private static readonly TimeSpan _lease = TimeSpan.FromSeconds(30);

    /// <summary>The compiled model of the TriggerApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release of TriggerApp that the fixture activated.</summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>The formatted entries of every log the server wrote.</summary>
    internal LogCollector Logs { get; } = new();

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with no sign-in. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync("triggers_a");
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
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("a"), OpenInstanceCounter.Instance);
            Assert.Empty(activated.Diagnostics);
            ReleaseId = compiled.Release!.Id;
            _model = compiled.Model!;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("cara", "Cara Clerk", "clerk"));
            builder.UseTestEndpointCallers(new TestIdentity("ticket-system", "Ticket System", "importer"));
            builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        });

        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenants:a:Hosts:0"] = Host,
                ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(_connectionString),
            })
            .Build();
        _worker = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace))
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" })
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>The qualified table of the <c>Ticket</c> entity.</summary>
    public string TicketTable
    {
        get
        {
            Assert.True(Model.TryGetEntity("Ticket", out var ticket));
            return EntityNaming.QualifiedTable(EntityNaming.Table(ticket.Id));
        }
    }

    /// <summary>The id of the process <paramref name="name"/> in the model.</summary>
    public Guid ProcessId(string name)
    {
        Assert.True(Model.TryGetProcess(name, out var process));
        return process.Id;
    }

    /// <summary>Runs <paramref name="sql"/>, a <c>SELECT count(*)</c>, with the named parameters.</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        (long)(await ScalarAsync(sql, parameters))!;

    /// <summary>Runs <paramref name="sql"/> with the named parameters and returns the first column of the first row.</summary>
    public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var scalar = await command.ExecuteScalarAsync();
        return scalar is DBNull ? null : scalar;
    }

    /// <summary>
    /// Starts <paramref name="process"/> for the ticket <paramref name="ticketId"/>, pinned to the
    /// fixture's release, as the start endpoint does: the instance is <c>running</c> at revision 1
    /// on the process's start step, with its first work item due a minute ago. Returns the instance id.
    /// </summary>
    public async Task<Guid> StartAsync(string process, Guid ticketId)
    {
        Assert.True(Model.TryGetProcess(process, out var model));
        Assert.True(Model.TryGetEntity("Ticket", out var ticket));
        var instanceId = Guid.CreateVersion7();
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Assert.True(await ProcessStarts.TryInsertInstanceAsync(
            transaction,
            new ProcessInstanceRow
            {
                Id = instanceId,
                ApplicationId = Model.Manifest.Id,
                ProcessId = model.Id,
                SubjectEntityId = ticket.Id,
                SubjectId = ticketId,
                ReleaseId = ReleaseId,
                State = ProcessStarts.Running,
                Revision = 1,
                Step = model.Start,
            }));
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            "a",
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            CancellationToken.None,
            processInstanceId: instanceId);
        await transaction.CommitAsync();
        return instanceId;
    }

    /// <summary>Claims and runs the due work items of the tenant until none is left.</summary>
    public async Task RunUntilIdleAsync(CancellationToken cancellationToken)
    {
        var runner = (_worker ?? throw new InvalidOperationException("The fixture is not initialized.")).GetRequiredService<WorkItemRunner>();
        while (await runner.RunNextAsync("a", _lease, cancellationToken))
        {
        }
    }

    /// <summary>The id of the one task of the instance <paramref name="instanceId"/>.</summary>
    public async Task<Guid> TaskIdAsync(Guid instanceId)
    {
        await using var command = DataSource.CreateCommand("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "The instance has no task.");
        var id = reader.GetGuid(0);
        Assert.False(await reader.ReadAsync(), "The instance has more than one task.");
        return id;
    }

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
