using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the applications ModuleTriggerApp and ModuleTriggerOtherApp
/// activated against the <c>reviews</c> and <c>followups</c> test modules under <c>Fixtures/Modules</c>.
/// Each application holds its own copy of the modules' tables and of the <c>followups</c> triggers, and
/// its own state of the schedule trigger <c>RemindFollowups</c>, which runs in the time zone of the
/// application: <c>Asia/Ho_Chi_Minh</c> for ModuleTriggerApp and <c>Europe/Berlin</c> for ModuleTriggerOtherApp. The real
/// server serves them on host <c>a.example.test</c> and signs in <c>clerk</c>, who holds the role
/// <c>clerk</c> in both, <c>operator</c>, who holds the operator role of the console, and <c>outsider</c>, who holds no role. The worker is the one of
/// <see cref="Steps"/>, which runs only when a test calls it, so a started instance stays
/// <c>running</c> until then.
/// </summary>
public sealed class ModuleTriggerFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ModuleTriggerApp.</summary>
    public static readonly Guid AppId = Guid.Parse("a5d2f8b1-6c37-4e09-b4a2-7f1c3e9d5b01");

    /// <summary>The <c>id</c> of ModuleTriggerOtherApp.</summary>
    public static readonly Guid OtherAppId = Guid.Parse("a5d2f8b1-6c37-4e09-b4a2-7f1c3e9d5c01");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;
    private ApplicationModel? _otherModel;

    /// <summary>The database, the activations and the worker. Its model is ModuleTriggerApp's.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleTriggerApp"),
        Path.Combine(_fixtures, "Modules"));

    /// <summary>The compiled model of ModuleTriggerOtherApp.</summary>
    public ApplicationModel OtherModel => _otherModel ?? throw new InvalidOperationException("The fixture is not initialized.");

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    public async ValueTask InitializeAsync()
    {
        await Steps.InitializeAsync();
        (_, _otherModel) = await Steps.ActivateAsync(Path.Combine(_fixtures, "ModuleTriggerOtherApp"), []);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(Steps.ConnectionString);
            builder.UseSetting($"Tenants:{Steps.Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Steps.Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(Steps.ConnectionString));
            builder.UseSetting($"Tenants:{Steps.Tenant}:SchemaConnectionString", Steps.ConnectionString);
            builder.UseSetting("Console:OperatorRole", "operator");
            builder.UseTestOidc(Steps.Tenant);
            builder.UseTestAuthentication(
                new TestIdentity("clerk", "Clerk User")
                {
                    ApplicationRoles = new Dictionary<Guid, IReadOnlyList<string>>
                    {
                        [AppId] = ["clerk"],
                        [OtherAppId] = ["clerk"],
                    },
                },
                new TestIdentity("operator", "Operator User", "operator"),
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

    /// <summary>Inserts a review with no state in the application's copy of the module's table and returns its id.</summary>
    public async Task<Guid> InsertReviewAsync(ApplicationModel model)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table(model, "reviews/Review")} ({Column("id")}, {Column("title")})
            VALUES (@id, @title)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Review {id:N}");
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The <c>state</c> of the record <paramref name="id"/> of <paramref name="entity"/>, or <see langword="null"/> when it has none.</summary>
    public async Task<string?> StateAsync(ApplicationModel model, string entity, Guid id) =>
        (string?)await Steps.ScalarAsync(
            $"SELECT {Column("state")} FROM {Table(model, entity)} WHERE {Column("id")} = @id",
            ("id", id));

    /// <summary>How many rows of <paramref name="entity"/> have the <c>title</c> <paramref name="title"/>.</summary>
    public Task<long> CountByTitleAsync(ApplicationModel model, string entity, string title) =>
        Steps.CountAsync(
            $"SELECT count(*) FROM {Table(model, entity)} WHERE {Column("title")} = @title",
            ("title", title));

    /// <summary>How many instances of the process <paramref name="process"/> of <paramref name="model"/> exist, on <paramref name="subject"/> when it is given.</summary>
    public Task<long> InstanceCountAsync(ApplicationModel model, string process, Guid? subject = null)
    {
        Assert.True(model.TryGetProcess(process, out var found));
        return subject is { } id
            ? Steps.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND application_id = @app AND subject_id = @subject",
                ("process", found.Id), ("app", model.Manifest.Id), ("subject", id))
            : Steps.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND application_id = @app",
                ("process", found.Id), ("app", model.Manifest.Id));
    }

    private static string Table(ApplicationModel model, string entity)
    {
        Assert.True(model.TryGetEntity(entity, out var found));
        return EntityNaming.QualifiedTable(EntityNaming.Table(found.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));
}
