using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the TriggerApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The test authentication scheme signs in <c>cara</c>, who holds <c>clerk</c>, and the test
/// endpoint caller scheme signs in <c>ticket-system</c>, who holds <c>importer</c>. No worker runs,
/// so a started instance stays <c>running</c>. The server's log entries go to <see cref="Logs"/>.
/// </summary>
public sealed class TriggerApiFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TriggerApp");

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ApplicationModel? _model;

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

        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        {
            var compiled = await ReleaseCompiler.CompileAsync(_appFolder, configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("a"));
            Assert.Empty(activated.Diagnostics);
            ReleaseId = compiled.Release!.Id;
            _model = compiled.Model!;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", _connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("cara", "Cara Clerk", "clerk"));
            builder.UseTestEndpointCallers(new TestIdentity("ticket-system", "Ticket System", "importer"));
            builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        });
    }

    public async ValueTask DisposeAsync()
    {
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

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
