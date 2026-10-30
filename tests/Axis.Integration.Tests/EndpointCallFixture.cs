using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the EndpointApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The test endpoint caller scheme signs in <c>importer-system</c> and <c>other-importer</c>, which
/// hold <c>importer</c>, and <c>reader-system</c>, which holds <c>reader</c>. No worker runs, so a started instance stays
/// <c>running</c>.
/// </summary>
public sealed class EndpointCallFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "EndpointApp");

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private ApplicationModel? _model;

    /// <summary>The compiled model of the EndpointApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release of EndpointApp that the fixture activated.</summary>
    public Guid ReleaseId { get; private set; }

    /// <summary>The qualified table of the <c>Note</c> entity.</summary>
    public string NoteTable => Table("Note");

    /// <summary>The qualified table of the <c>NoteLine</c> child entity.</summary>
    public string NoteLineTable => Table("NoteLine");

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with no caller. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _connectionString = await _database.CreateDatabaseAsync("endpoints_a");
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
            await PostgreSqlServer.GrantRuntimeRoleAsync(_connectionString);
        }

        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            var compiled = await ReleaseCompiler.CompileAsync(_appFolder, configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, new ActivationBindings(
                "a",
                new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AnnounceHookUrl"] = new(ActivationBinding.UrlKind, new Uri("https://hooks.example.test/announce")),
                    ["AnnounceHookSecret"] = new(ActivationBinding.SecretKind, null),
                },
                AllowHttp: false),
                OpenInstanceCounter.Instance);
            Assert.Empty(activated.Diagnostics);
            ReleaseId = compiled.Release!.Id;
            _model = compiled.Model!;
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestEndpointCallers(
                new TestIdentity("importer-system", "Importer System", "importer"),
                new TestIdentity("other-importer", "Other Importer", "importer"),
                new TestIdentity("reader-system", "Reader System", "reader"));
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

    /// <summary>Runs <paramref name="sql"/>, a <c>SELECT count(*)</c>, with the named parameters.</summary>
    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = DataSource.CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>The quoted column of <paramref name="field"/>, or of the record id for <c>id</c>.</summary>
    public static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private string Table(string entityName)
    {
        Assert.True(Model.TryGetEntity(entityName, out var entity));
        return EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));
    }

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
