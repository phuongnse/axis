using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleProcessApp fixture activated against the <c>reviews</c>
/// test module under <c>Fixtures/Modules</c>. The application holds the module's copy of its
/// <c>Review</c> entity, and maps the module's role slot <c>approver</c> to the role <c>manager</c>. The real server
/// serves it on host <c>a.example.test</c> and signs in <c>clerk</c>, who holds the role <c>clerk</c> in the application,
/// <c>manager</c>, who holds <c>manager</c>, <c>elsewhere</c>, who holds <c>manager</c> in another application only,
/// and <c>outsider</c>, who holds no role. The worker is the one of <see cref="Steps"/>, which
/// runs only when a test calls it, so a started instance stays <c>running</c> until then.
/// </summary>
public sealed class ModuleProcessFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ModuleProcessApp.</summary>
    public static readonly Guid AppId = Guid.Parse("9b4d6e83-2a5c-4f17-8d60-3e7a1c9b5f01");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database, the activations and the worker.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleProcessApp"),
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
                new TestIdentity("clerk", "Clerk User")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [AppId] = ["clerk"] },
                },
                new TestIdentity("manager", "Manager User")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [AppId] = ["manager"] },
                },
                new TestIdentity("elsewhere", "Manager Elsewhere")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>> { [Guid.Parse("9b4d6e83-2a5c-4f17-8d60-3e7a1c9b5fff")] = ["manager"] },
                },
                new TestIdentity("outsider", "Outsider No Role"));
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

    /// <summary>Inserts an order for the review <paramref name="review"/> and returns its id.</summary>
    public async Task<Guid> InsertOrderAsync(Guid review)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table("Order")} ({Column("id")}, {Column("title")}, {Column("review")})
            VALUES (@id, @title, @review)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Order {id:N}");
        command.Parameters.AddWithValue("review", review);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>Inserts a review with no state in the application's copy of the module's table and returns its id.</summary>
    public async Task<Guid> InsertReviewAsync()
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table("reviews/Review")} ({Column("id")}, {Column("title")})
            VALUES (@id, @title)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Review {id:N}");
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The <c>state</c> of the review <paramref name="id"/>, or <see langword="null"/> when it has none.</summary>
    public async Task<string?> ReviewStateAsync(Guid id) =>
        (string?)await Steps.ScalarAsync(
            $"SELECT {Column("state")} FROM {Table("reviews/Review")} WHERE {Column("id")} = @id",
            ("id", id));

    private string Table(string entity)
    {
        Assert.True(Steps.Model.TryGetEntity(entity, out var model));
        return EntityNaming.QualifiedTable(EntityNaming.Table(model.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));
}
