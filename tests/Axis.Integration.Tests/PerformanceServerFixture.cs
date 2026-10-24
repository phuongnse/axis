using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration and data migrations applied and one fixture
/// application activated, served by the real server on host <c>a.example.test</c>. The test
/// authentication scheme signs in <c>anna</c> with one role. A subclass names the application folder
/// and the role, and may seed rows once the application is active. <see cref="Client"/> is signed in as
/// <c>anna</c>.
/// </summary>
public abstract class PerformanceServerFixture(string application, string role) : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private readonly PostgreSqlFixture _database = new();
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private ApplicationModel? _model;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The compiled model of the fixture application.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        var connectionString = await _database.CreateDatabaseAsync("performance_a");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
            await using var data = DataDatabaseFixture.CreateContext(connection);
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString);

            var compiled = await ReleaseCompiler.CompileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", application), configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance);
            Assert.Empty(activated.Diagnostics);
            _model = compiled.Model!;
        }

        await SeedAsync(_dataSource, _model);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna Analyst", role));
        });
        _client = _factory.CreateClient().SignedInAs("anna");
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
        GC.SuppressFinalize(this);
    }

    /// <summary>Inserts the rows of the test once the application is active. The default inserts none.</summary>
    protected virtual Task SeedAsync(NpgsqlDataSource dataSource, ApplicationModel model) => Task.CompletedTask;

    protected static string Table(EntityModel entity) => EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id));

    protected static string Column(string field) => EntityNaming.Quote(EntityNaming.Column(field));
}
