using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, served by the real server on host <c>a.example.test</c>. The
/// server migrates it and activates the fixture applications ModuleAppA, ModuleAppB, ModuleContractApp, ModuleCeilingApp and ModuleRelaxApp through
/// <c>ActivateOnStartup</c>. Both use the <c>departments</c> test module installed under
/// <c>Fixtures/Modules</c>, so the module's department table and seed rows exist once for each.
/// ModuleAppA also declares the <c>budgets</c> module, which depends on <c>departments</c>, and has an
/// <c>Employee</c> entity that references a department. Both also declare the <c>approvals</c> module, whose
/// <c>approver</c> slot ModuleAppA maps to <c>manager</c> and ModuleAppB to <c>reviewer</c>. The test
/// authentication scheme signs in <c>requester</c>, an employee, <c>manager</c>, who holds no role, and
/// <c>approver-a</c>, who holds <c>manager</c> in ModuleAppA only.
/// ModuleAppA also holds the site <c>modulesa</c>, which lists the <c>departments</c> module's Departments page.
/// ModuleContractApp declares only the <c>requests</c> module, whose <c>Request</c> entity has a contract with a
/// lifecycle, and maps its <c>requester</c> slot to <c>employee</c>.
/// ModuleCeilingApp declares only the <c>claims</c> module, whose <c>Claim</c> entity has a contract with a grant
/// ceiling. It maps the <c>filer</c> slot to <c>clerk</c> and the <c>viewer</c> slot to <c>auditor</c>. The test
/// authentication scheme signs in <c>ceiling-clerk</c> and <c>ceiling-auditor</c>, who hold those roles there only.
/// ModuleRelaxApp declares the <c>requests</c>, <c>stages</c>, <c>ledgers</c> and <c>claims</c> modules, each with
/// one relax entry: the <c>Request</c> submit guard, the <c>Ticket</c> <c>amountFrozenAfterSubmit</c> validation, the
/// <c>ledgers</c> <c>Claim</c> <c>entryRecorded</c> effect and the <c>claims</c> <c>viewer</c> ceiling slot. The test
/// authentication scheme signs in <c>relax-clerk</c> and <c>relax-auditor</c>, who hold <c>clerk</c> and <c>auditor</c> there only.
/// </summary>
public sealed class ModuleAppFixture : IAsyncLifetime
{
    public const string Tenant = "a";

    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ModuleAppA.</summary>
    public static readonly Guid AppAId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3a01");

    /// <summary>The <c>id</c> of ModuleAppB.</summary>
    public static readonly Guid AppBId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3b01");

    /// <summary>The <c>id</c> of ModuleCeilingApp.</summary>
    public static readonly Guid CeilingAppId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3d01");

    /// <summary>The <c>id</c> of ModuleRelaxApp.</summary>
    public static readonly Guid RelaxAppId = Guid.Parse("7a2f4c61-1b3d-4e58-8a9c-5d0e2f7b3e01");

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
            builder.UseSetting("ActivateOnStartup:2", Path.Combine(_fixtures, "ModuleContractApp"));
            builder.UseSetting("ActivateOnStartup:3", Path.Combine(_fixtures, "ModuleCeilingApp"));
            builder.UseSetting("ActivateOnStartup:4", Path.Combine(_fixtures, "ModuleRelaxApp"));
            builder.UseTestAuthentication(
                new TestIdentity("requester", "Requester User", "employee"),
                new TestIdentity("manager", "Manager No Role"),
                new TestIdentity("approver-a", "Approver In A")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [AppAId] = ["manager"] },
                },
                new TestIdentity("ceiling-clerk", "Ceiling Clerk")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [CeilingAppId] = ["clerk"] },
                },
                new TestIdentity("ceiling-auditor", "Ceiling Auditor")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [CeilingAppId] = ["auditor"] },
                },
                new TestIdentity("relax-clerk", "Relax Clerk")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [RelaxAppId] = ["clerk"] },
                },
                new TestIdentity("relax-auditor", "Relax Auditor")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [RelaxAppId] = ["auditor"] },
                });
        });

        // Creating a client starts the server, which migrates the database, activates the applications and seeds them.
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
