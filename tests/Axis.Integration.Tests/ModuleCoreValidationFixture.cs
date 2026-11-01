using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleCoreValidationApp fixture activated against the <c>stages</c>
/// test module under <c>Fixtures/Modules</c>. The module's <c>Ticket</c> entity has the validation
/// <c>amountNotNegative</c>, a contract with a lifecycle on <c>status</c> and the core validations
/// <c>amountFrozenAfterSubmit</c> and <c>stepMovesWithNewDecision</c>. The application maps the role slot
/// <c>requester</c> to <c>employee</c> and adds the process <c>AdvanceTwice</c>. The real server serves it on host
/// <c>a.example.test</c> and signs in <c>requester</c>, who holds <c>employee</c>. The worker is the one of
/// <see cref="Steps"/>, which runs only when a test calls it, so a started instance stays <c>running</c> until then.
/// </summary>
public sealed class ModuleCoreValidationFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database, the activations and the worker.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleCoreValidationApp"),
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

    /// <summary>Inserts a draft ticket, which skips the contract, and returns its id.</summary>
    public async Task<Guid> InsertTicketAsync(string title)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table()} ({Column("id")}, {Column("title")}, {Column("status")})
            VALUES (@id, @title, 'draft')
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", title);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The amount, the step and the version of the ticket <paramref name="id"/>.</summary>
    public async Task<(long? Amount, long? Step, long Version)> TicketAsync(Guid id)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("amount")}, {Column("step")}, {Column("version")} FROM {Table()} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            await reader.IsDBNullAsync(0) ? null : reader.GetInt64(0),
            await reader.IsDBNullAsync(1) ? null : reader.GetInt64(1),
            reader.GetInt64(2));
    }

    private string Table()
    {
        Assert.True(Steps.Model.TryGetEntity("stages/Ticket", out var model));
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
