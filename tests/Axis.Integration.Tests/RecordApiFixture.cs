using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Axis.Data.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Integration.Tests;

/// <summary>
/// Two tenant databases in one PostgreSQL container, each with both migrations applied and the
/// RecordsApp fixture application activated, served by the real server on hosts
/// <c>a.example.test</c> and <c>b.example.test</c>. Tenant A also has a compiled release of an
/// application that was never activated.
/// </summary>
public sealed class RecordApiFixture : IAsyncLifetime
{
    public const string TenantA = "a";
    public const string TenantB = "b";
    public const string HostA = "a.example.test";
    public const string HostB = "b.example.test";

    private readonly PostgreSqlFixture _database = new();
    private readonly Dictionary<string, string> _connectionStrings = [];
    private readonly Dictionary<string, NpgsqlDataSource> _dataSources = [];
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private ApplicationModel? _model;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The compiled model of the RecordsApp fixture, the same in both tenants.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The name of an application compiled in tenant A but never activated.</summary>
    public string DormantApp { get; } = $"Dormant{Guid.NewGuid():N}";

    public string ConnectionString(string tenant) => _connectionStrings[tenant];

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            var connectionString = await CreateDatabaseAsync($"records_{tenant}");
            _connectionStrings[tenant] = connectionString;
            _dataSources[tenant] = NpgsqlDataSource.Create(connectionString);
            await using var configuration = CreateConfigurationContext(connectionString);
            await using var data = CreateDataContext(connectionString);
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();

            // Releases live in the tenant database, so the fixture is compiled once per tenant.
            var compiled = await ReleaseCompiler.CompileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "RecordsApp"), configuration);
            Assert.Empty(compiled.Diagnostics);
            var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data);
            Assert.Empty(activated.Diagnostics);
            _model = compiled.Model;

            if (tenant == TenantA)
            {
                using var dormant = new TemporaryFolder().With(
                    "application.json",
                    $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "{{DormantApp}}", "formatVersion": 1 }""");
                var dormantCompiled = await ReleaseCompiler.CompileAsync(dormant.Path, configuration);
                Assert.Empty(dormantCompiled.Diagnostics);
                Assert.NotNull(dormantCompiled.Release);
            }
        }

        // The server does not migrate tenant databases; both were migrated above.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", ConnectionString(TenantA));
            builder.UseSetting("Tenants:a:Hosts:0", HostA);
            builder.UseSetting("Tenants:a:ConnectionString", ConnectionString(TenantA));
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", ConnectionString(TenantB));
        });
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        foreach (var dataSource in _dataSources.Values)
        {
            await dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>
    /// Inserts a row of <paramref name="entityName"/> in <paramref name="tenant"/> and returns its id.
    /// Each value is text cast to the field's column type in SQL, so no Npgsql type mapping is involved.
    /// </summary>
    public async Task<Guid> InsertAsync(string tenant, string entityName, IReadOnlyDictionary<string, string?> values)
    {
        Assert.True(Model.TryGetEntity(entityName, out var entity));
        var id = Guid.NewGuid();
        var columns = new List<string> { EntityNaming.Quote(EntityNaming.IdColumn) };
        var parameters = new List<string> { "@id" };
        await using var command = _dataSources[tenant].CreateCommand();
        command.Parameters.AddWithValue("id", id);
        foreach (var (name, value) in values)
        {
            var field = entity.Fields.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            var parameter = $"p{columns.Count - 1}";
            columns.Add(EntityNaming.Quote(EntityNaming.Column(field.Name)));
            parameters.Add($"@{parameter}::{ColumnTypes.Render(field)}");
            command.Parameters.Add(new NpgsqlParameter(parameter, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value });
        }

        command.CommandText =
            $"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)})";
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Deletes every row of the fixture entities in both tenants; items first, as they reference departments.</summary>
    public async Task ResetAsync()
    {
        Assert.True(Model.TryGetEntity("Item", out var item));
        Assert.True(Model.TryGetEntity("Department", out var department));
        foreach (var dataSource in _dataSources.Values)
        {
            foreach (var entity in new[] { item, department })
            {
                await using var command = dataSource.CreateCommand($"DELETE FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity.Id))}");
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    public static HttpRequestMessage Request(string path, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return request;
    }

    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var dataSource = NpgsqlDataSource.Create(_database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""CREATE DATABASE "{name}" """);
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = name }.ConnectionString;
    }

    private static ConfigurationDbContext CreateConfigurationContext(string connectionString) =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);

    private static DataDbContext CreateDataContext(string connectionString) =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(connectionString).Options);
}
