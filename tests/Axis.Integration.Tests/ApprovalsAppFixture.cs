using System.Security.Cryptography;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, served by the real server on host <c>a.example.test</c>. The server migrates it and
/// activates the fixture application ApprovalsApp through <c>ActivateOnStartup</c>. The tenant has a data key, because
/// <c>phone</c> is sensitive. The application declares the built-in modules <c>organization</c> and <c>approvals</c>,
/// which the test project copies to <c>BuiltInModules</c>, maps the slot <c>admin</c> of each to <c>admin</c> and the
/// slot <c>member</c> of each to <c>employee</c>, and has one policy of its own that grants access to the entities of
/// <c>approvals</c>. The test authentication scheme signs in <c>appr-admin</c>, who holds <c>admin</c> there, and the
/// members <c>appr-ann</c>, <c>appr-bob</c> and <c>appr-carl</c>, who hold <c>employee</c> there. A member's employee
/// record carries the member's id in <c>user</c>, which is unique, so each test uses its own member.
/// </summary>
public sealed class ApprovalsAppFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ApprovalsApp.</summary>
    public static readonly Guid AppId = Guid.Parse("140c35f8-24e6-4c90-80a7-3dadf905e3d9");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string[] _members = ["appr-ann", "appr-bob", "appr-carl"];

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
        var connectionString = await _database.CreateDatabaseAsync("approvals_a");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", Host);
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", connectionString);
            builder.UseSetting("Tenants:a:DataKeys:1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseTestOidc("a");
            builder.UseSetting("Modules:Path", Path.Combine(AppContext.BaseDirectory, "BuiltInModules"));
            builder.UseSetting("ActivateOnStartup:0", Path.Combine(_fixtures, "ApprovalsApp"));
            builder.UseTestAuthentication(
                [
                    new TestIdentity("appr-admin", "Approvals Admin") { ApplicationRoles = Roles("admin") },
                    .. _members.Select(id => new TestIdentity(id, id) { ApplicationRoles = Roles("employee") }),
                ]);
        });

        // Creating a client starts the server, which migrates the database and activates the application.
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

    private static Dictionary<Guid, IReadOnlyList<string>> Roles(string role) => new() { [AppId] = [role] };
}
