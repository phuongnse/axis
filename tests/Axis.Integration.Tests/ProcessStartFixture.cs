using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the configuration, data and processes migrations applied
/// and the ProcessApp fixture activated, served by the real server on host <c>a.example.test</c>.
/// The server knows the test user <c>anna</c>.
/// </summary>
public sealed class ProcessStartFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _appFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ProcessApp");

    private readonly PostgreSqlFixture _database = new();
    private string? _connectionString;
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private ApplicationModel? _model;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    /// <summary>The compiled model of the ProcessApp fixture.</summary>
    public ApplicationModel Model => _model ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>The release of ProcessApp that the fixture activated first.</summary>
    public Guid FirstReleaseId { get; private set; }

    private NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await using (var admin = NpgsqlDataSource.Create(_database.ConnectionString))
        await using (var create = admin.CreateCommand("""CREATE DATABASE "processes_a" """))
        {
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "processes_a" }.ConnectionString;
        _dataSource = NpgsqlDataSource.Create(_connectionString);
        await using (var configuration = CreateConfigurationContext())
        await using (var data = CreateDataContext())
        await using (var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(_connectionString).Options))
        {
            await configuration.Database.MigrateAsync();
            await data.Database.MigrateAsync();
            await processes.Database.MigrateAsync();
        }

        var (releaseId, model) = await ActivateAsync(_appFolder);
        FirstReleaseId = releaseId;
        _model = model;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", _connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", _connectionString);
            builder.UseSetting("TestUsers:0:Id", "anna");
            builder.UseSetting("TestUsers:0:DisplayName", "Anna Employee");
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

        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    /// <summary>Inserts a request with a new title and <paramref name="status"/>, and returns its id.</summary>
    public async Task<Guid> InsertRequestAsync(string? status)
    {
        var id = Guid.NewGuid();
        await using var command = DataSource.CreateCommand(
            $"INSERT INTO {RequestTable} ({Column("id")}, {Column("title")}, {Column("status")}) VALUES (@id, @title, @status)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Request {id:N}");
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Sets the status of the request <paramref name="id"/>.</summary>
    public async Task SetStatusAsync(Guid id, string? status)
    {
        await using var command = DataSource.CreateCommand($"UPDATE {RequestTable} SET {Column("status")} = @status WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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

    /// <summary>
    /// Compiles and activates a copy of ProcessApp with one text changed, so its content hash and
    /// its release are new. Returns the new release's id.
    /// </summary>
    public async Task<Guid> ActivateChangedReleaseAsync()
    {
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(_appFolder, "*", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(file);
            folder.With(Path.GetRelativePath(_appFolder, file), content.Replace(
                "This request cannot be submitted.",
                $"This request cannot be submitted ({Guid.NewGuid():N}).",
                StringComparison.Ordinal));
        }

        var (releaseId, _) = await ActivateAsync(folder.Path);
        Assert.NotEqual(FirstReleaseId, releaseId);
        return releaseId;
    }

    /// <summary>The id of the release active for ProcessApp now.</summary>
    public async Task<Guid> ActiveReleaseIdAsync()
    {
        await using var configuration = CreateConfigurationContext();
        var active = await new ActiveReleaseStore(configuration).FindByNameAsync(Model.Manifest.Name);
        return Assert.IsType<ActiveRelease>(active).ReleaseId;
    }

    private async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(string folder)
    {
        await using var configuration = CreateConfigurationContext();
        await using var data = CreateDataContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder, configuration);
        Assert.Empty(compiled.Diagnostics);
        var activated = await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data);
        Assert.Empty(activated.Diagnostics);
        return (compiled.Release!.Id, compiled.Model!);
    }

    private string RequestTable
    {
        get
        {
            Assert.True(Model.TryGetEntity("Request", out var request));
            return EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));
        }
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private ConfigurationDbContext CreateConfigurationContext() =>
        new(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(_connectionString).Options);

    private DataDbContext CreateDataContext() =>
        new(new DbContextOptionsBuilder<DataDbContext>().UseNpgsql(_connectionString).Options);
}
