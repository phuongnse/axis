using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ModuleEffectApp fixture activated against the <c>ledgers</c> test module
/// under <c>Fixtures/Modules</c>. The module's <c>Claim</c> entity has a contract with a lifecycle on <c>status</c>
/// and the effect <c>entryRecorded</c>, which creates an <c>Entry</c> when a claim is approved. The <c>Entry</c> entity
/// refuses a negative amount and starts in <c>open</c>. The tests call the shared record update directly, so no
/// server runs.
/// </summary>
public sealed class ModuleEffectFixture : IAsyncLifetime
{
    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The database and the activations.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ModuleEffectApp"),
        Path.Combine(_fixtures, "Modules"));

    /// <summary>The module's <c>Claim</c> entity.</summary>
    public EntityModel ClaimEntity => Entity("ledgers/Claim");

    public ValueTask InitializeAsync() => Steps.InitializeAsync();

    public ValueTask DisposeAsync() => Steps.DisposeAsync();

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
