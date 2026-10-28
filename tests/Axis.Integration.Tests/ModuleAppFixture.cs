using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, served by the real server on host <c>a.example.test</c>. The
/// server migrates it and activates the fixture applications ModuleAppA and ModuleAppB through
/// <c>ActivateOnStartup</c>. Both use the <c>departments</c> test module installed under
/// <c>Fixtures/Modules</c>, so the module's department table and seed rows exist once for each.
/// ModuleAppA also has an <c>Employee</c> entity that references a department. The test
/// authentication scheme signs in <c>requester</c>, an employee, and <c>binh</c>, who holds no role.
/// </summary>
public sealed class ModuleAppFixture : IAsyncLifetime
{
    public const string Tenant = "a";

    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ModuleAppA.</summary>
    public static readonly Guid AppAId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3a01");

    /// <summary>The <c>id</c> of ModuleAppB.</summary>
    public static readonly Guid AppBId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3b01");

    /// <summary>The id of the Department entity in the module file, before it is derived for an application.</summary>
    public static readonly Guid DepartmentEntityId = Guid.Parse("3b7e5a10-4c2d-4f6a-9d18-7a1c0e5b2f01");

    /// <summary>The id of the DepartmentNumber sequence in the module file, before it is derived for an application.</summary>
    public static readonly Guid DepartmentSequenceId = Guid.Parse("3b7e5a10-4c2d-4f6a-9d18-7a1c0e5b2f02");

    /// <summary>The id of the module's seeded Finance department, the same in every application.</summary>
    public static readonly Guid Finance = Guid.Parse("8f1c3d52-6a4e-4b70-a1d9-2e5c7b9f0a11");

    /// <summary>The id of the module's seeded Engineering department, the same in every application.</summary>
    public static readonly Guid Engineering = Guid.Parse("8f1c3d52-6a4e-4b70-a1d9-2e5c7b9f0a12");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private readonly PostgreSqlFixture _database = new();
    private NpgsqlDataSource? _dataSource;
    private WebApplicationFactory<Program>? _factory;

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    /// <summary>A data source for the tenant database, for SQL checks.</summary>
    public NpgsqlDataSource DataSource => _dataSource ?? throw new InvalidOperationException("The fixture is not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        var connectionString = await _database.CreateDatabaseAsync("modules_a");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting($"Tenants:{Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting($"Tenants:{Tenant}:SchemaConnectionString", connectionString);
            builder.UseTestOidc(Tenant);
            builder.UseSetting("Modules:Path", Path.Combine(_fixtures, "Modules"));
            builder.UseSetting("ActivateOnStartup:0", Path.Combine(_fixtures, "ModuleAppA"));
            builder.UseSetting("ActivateOnStartup:1", Path.Combine(_fixtures, "ModuleAppB"));
            builder.UseTestAuthentication(
                new TestIdentity("requester", "Requester User", "employee"),
                new TestIdentity("binh", "Binh No Role"));
        });

        // Creating a client starts the server, which migrates the database, activates both applications and seeds them.
        _factory.CreateClient().Dispose();
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
}
