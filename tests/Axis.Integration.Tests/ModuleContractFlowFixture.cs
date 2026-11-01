using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleContractFlowApp fixture activated against the <c>requests</c>
/// test module under <c>Fixtures/Modules</c>. The module's <c>Request</c> entity has a contract with a lifecycle
/// on <c>status</c>: a request is created as a draft, and the transition <c>submit</c> has the guard
/// <c>amount &gt; 0</c>. The application maps the module's role slot <c>requester</c> to the role <c>employee</c>,
/// and adds a form, the processes <c>SubmitRequest</c> and <c>AutoSubmit</c>, the <c>import</c> grant and
/// the endpoint <c>CreateRequest</c>. The real server serves it on host <c>a.example.test</c> and signs in
/// <c>requester</c>, who holds <c>employee</c>. The endpoint caller is <c>request-importer</c>, who holds
/// <c>importer</c>, and <c>outsider-system</c> holds no role. The worker is the one of <see cref="Steps"/>, which
/// runs only when a test calls it, so a started instance stays <c>running</c> until then.
/// </summary>
public sealed class ModuleContractFlowFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database, the activations and the worker.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleContractFlowApp"),
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
            builder.UseTestAuthentication(new TestIdentity("requester", "Requester User", "employee"));
            builder.UseTestEndpointCallers(
                new TestIdentity("request-importer", "Request Importer", "importer"),
                new TestIdentity("outsider-system", "Outsider System"));
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
    public async Task<Guid> InsertRequestAsync(string title, long amount, string status)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table()} ({Column("id")}, {Column("title")}, {Column("amount")}, {Column("status")})
            VALUES (@id, @title, @amount, @status)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The number of requests that have the title <paramref name="title"/>.</summary>
    public Task<long> CountRequestsAsync(string title) =>
        Steps.CountAsync(
            $"SELECT count(*) FROM {Table()} WHERE {Column("title")} = @title",
            ("title", title));

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
        Assert.True(Steps.Model.TryGetEntity("requests/Request", out var model));
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
