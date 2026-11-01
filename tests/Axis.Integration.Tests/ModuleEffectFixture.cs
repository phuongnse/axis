using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleEffectApp fixture activated against the <c>ledgers</c> test module
/// under <c>Fixtures/Modules</c>. The module's <c>Claim</c> entity has a contract with a lifecycle on <c>status</c>
/// and the effect <c>entryRecorded</c>, which creates an <c>Entry</c> when a claim is approved. The <c>Entry</c> entity
/// refuses a negative amount and starts in <c>open</c>, and the module's create trigger <c>NoteNewEntries</c> starts
/// the process <c>NoteEntry</c> for each new entry. Some tests call the shared record update directly. The real server
/// also serves the application on host <c>a.example.test</c> and signs in <c>clerk</c>, who holds the role <c>clerk</c>,
/// and <c>outsider</c>, who holds no role. The worker is the one of <see cref="Steps"/>, which runs only when a test
/// calls it, so a started instance stays <c>running</c>.
/// </summary>
public sealed class ModuleEffectFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ModuleEffectApp.</summary>
    public static readonly Guid AppId = Guid.Parse("6466d2e0-85ae-48ce-a6fe-f0bc10b20435");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database and the activations.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleEffectApp"),
        Path.Combine(_fixtures, "Modules"));

    /// <summary>The module's <c>Claim</c> entity.</summary>
    public EntityModel ClaimEntity => Entity("ledgers/Claim");

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

    /// <summary>Inserts a claim in <paramref name="status"/>, which skips the contract, and returns its id.</summary>
    public async Task<Guid> InsertClaimAsync(string status, long? amount)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table("ledgers/Claim")} ({Column("id")}, {Column("title")}, {Column("amount")}, {Column("status")})
            VALUES (@id, @title, @amount, @status)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("title", $"Claim {id:N}");
        command.Parameters.AddWithValue("amount", amount is null ? DBNull.Value : amount);
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The status and the version of the claim <paramref name="id"/>.</summary>
    public async Task<(string Status, long Version)> ClaimAsync(Guid id)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("status")}, {Column("version")} FROM {Table("ledgers/Claim")} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetInt64(1));
    }

    /// <summary>The entries that refer to the claim <paramref name="claim"/>, as their amount, creator and status.</summary>
    public async Task<IReadOnlyList<(long? Amount, string? CreatedBy, string Status)>> EntriesAsync(Guid claim)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("amount")}, {Column("createdBy")}, {Column("status")} FROM {Table("ledgers/Entry")} WHERE {Column("claim")} = @claim");
        command.Parameters.AddWithValue("claim", claim);
        await using var reader = await command.ExecuteReaderAsync();
        var entries = new List<(long?, string?, string)>();
        while (await reader.ReadAsync())
        {
            entries.Add((
                await reader.IsDBNullAsync(0) ? null : reader.GetInt64(0),
                await reader.IsDBNullAsync(1) ? null : reader.GetString(1),
                reader.GetString(2)));
        }

        return entries;
    }

    /// <summary>The ids of the entries that refer to the claim <paramref name="claim"/>.</summary>
    public async Task<IReadOnlyList<Guid>> EntryIdsAsync(Guid claim)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("id")} FROM {Table("ledgers/Entry")} WHERE {Column("claim")} = @claim");
        command.Parameters.AddWithValue("claim", claim);
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<Guid>();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    /// <summary>The audit records of the record <paramref name="recordId"/>, as their actor, action and details JSON.</summary>
    public async Task<IReadOnlyList<(string Actor, string Action, string Details)>> AuditsAsync(Guid recordId)
    {
        await using var command = Steps.DataSource.CreateCommand(
            """SELECT "actor", "action", "details"::text FROM "axis"."audit_records" WHERE "record_id" = @record ORDER BY "occurred_at", "id" """);
        command.Parameters.AddWithValue("record", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        var audits = new List<(string, string, string)>();
        while (await reader.ReadAsync())
        {
            audits.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return audits;
    }

    /// <summary>How many process instances of ModuleEffectApp have <paramref name="subject"/> as their subject.</summary>
    public Task<long> InstanceCountAsync(Guid subject) =>
        Steps.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE application_id = @app AND subject_id = @subject",
            ("app", Steps.Model.Manifest.Id), ("subject", subject));

    /// <summary>How many process instances of ModuleEffectApp exist.</summary>
    public Task<long> InstanceCountAsync() =>
        Steps.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE application_id = @app",
            ("app", Steps.Model.Manifest.Id));

    private EntityModel Entity(string name)
    {
        Assert.True(Steps.Model.TryGetEntity(name, out var model));
        return model;
    }

    private string Table(string entity) => EntityNaming.QualifiedTable(EntityNaming.Table(Entity(entity).Id));

    private static string Column(string field) =>
        EntityNaming.Quote(field switch
        {
            "id" => EntityNaming.IdColumn,
            "version" => EntityNaming.VersionColumn,
            _ => EntityNaming.Column(field),
        });
}
