using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleAccountabilityApp fixture activated against the <c>signoffs</c>
/// test module under <c>Fixtures/Modules</c>. The module's <c>Request</c> entity has a contract with a lifecycle
/// on <c>status</c>, and the transition <c>approve</c> has the guard
/// <c>accountableUser() != null and accountableUser() != requester</c>. The application maps the role slots
/// <c>requester</c> to <c>employee</c> and <c>approver</c> to <c>approver</c>, and adds the processes
/// <c>ApproveAtOnce</c> and <c>ReviewThenApprove</c> and the schedule trigger <c>ApproveDaily</c>. The real
/// server serves it on host <c>a.example.test</c> and signs in <c>requester</c>, who holds <c>employee</c>,
/// <c>approver</c>, who holds <c>approver</c>, and <c>outsider</c>, who holds no role. The worker is the one of
/// <see cref="Steps"/>, which runs only when a test calls it, so a started instance stays <c>running</c> until then.
/// </summary>
public sealed class ModuleAccountabilityFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database, the activations and the worker.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleAccountabilityApp"),
        Path.Combine(_fixtures, "Modules"));

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await Steps.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(Steps.ConnectionString);
            builder.UseSetting($"Tenants:{Steps.Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Steps.Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(Steps.ConnectionString));
            builder.UseSetting($"Tenants:{Steps.Tenant}:SchemaConnectionString", Steps.ConnectionString);
            builder.UseTestOidc(Steps.Tenant);
            builder.UseTestAuthentication(
                new TestIdentity("requester", "Requester User", "employee"),
                new TestIdentity("approver", "Approver User", "approver"),
                new TestIdentity("outsider", "Outsider User"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Steps.DisposeAsync();
    }

    /// <summary>Inserts a request with the given values, which skips the contract, and returns its id.</summary>
    public async Task<Guid> InsertRequestAsync(string title, string requester, string status)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table()} ({Column("id")}, {Column("title")}, {Column("requester")}, {Column("status")})
            VALUES (@id, @title, @requester, @status)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("requester", requester);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The status and the version of the request <paramref name="id"/>.</summary>
    public async Task<(string Status, long Version)> RequestAsync(Guid id)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("status")}, {Column("version")} FROM {Table()} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1));
    }

    private string Table()
    {
        Assert.True(Steps.Model.TryGetEntity("signoffs/Request", out var model));
        return EntityNaming.QualifiedTable(EntityNaming.Table(model.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field switch
        {
            "id" => EntityNaming.IdColumn,
            "version" => EntityNaming.VersionColumn,
            _ => EntityNaming.Column(field),
        });
}
