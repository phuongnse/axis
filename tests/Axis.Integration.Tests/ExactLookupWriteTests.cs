using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A write of an exact lookup field stores its lookup hash in the <c>_lookup</c> column in the same statement: the number of the
/// newest lookup key and the HMAC-SHA256 of the plain value, or NULL for a null value.
/// </summary>
public sealed class ExactLookupWriteTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _personId = Guid.NewGuid();
    private readonly Guid _cardId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";
    private readonly Dictionary<int, byte[]> _lookupKeys = [];

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_and_an_update_store_the_lookup_hash_and_a_null_value_stores_null()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;

        var created = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Ann", "nationalId": "079-123", "passport": "P1" } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var id = created.Record!.Id;
        Assert.Equal(Hash(1, _personId, "nationalId", "079-123"), await ReadAsync(_personId, id, "nationalId"));

        var updated = await UpdateAsync(connection, compiled, person, cipher, id, """{ "version": 1, "values": { "nationalId": "999-000" } }""");

        Assert.Equal(RecordCommandOutcome.Written, updated.Outcome);
        Assert.Equal(Hash(1, _personId, "nationalId", "999-000"), await ReadAsync(_personId, id, "nationalId"));
        Assert.Equal(Hash(1, _personId, "passport", "P1"), await ReadAsync(_personId, id, "passport"));

        var cleared = await UpdateAsync(connection, compiled, person, cipher, id, """{ "version": 2, "values": { "nationalId": null } }""");

        Assert.Equal(RecordCommandOutcome.Written, cleared.Outcome);
        Assert.Null(await ReadAsync(_personId, id, "nationalId"));
        Assert.Equal(Hash(1, _personId, "passport", "P1"), await ReadAsync(_personId, id, "passport"));
    }

    [Fact]
    public async Task A_write_stores_the_number_of_the_newest_lookup_key_and_the_hmac_under_it()
    {
        var cipher = Cipher(1, 2);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;

        var created = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Ann", "nationalId": "079-123" } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var stored = (await ReadAsync(_personId, created.Record!.Id, "nationalId"))!;
        Assert.Equal([0, 0, 0, 2], stored[..4]);
        Assert.Equal(Hash(2, _personId, "nationalId", "079-123"), stored);
        Assert.NotEqual(Hash(1, _personId, "nationalId", "079-123")[4..], stored[4..]);
    }

    [Fact]
    public async Task Equal_values_in_two_exact_lookup_fields_store_different_hashes()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;

        var created = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Ann", "nationalId": "same", "passport": "same" } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var nationalId = await ReadAsync(_personId, created.Record!.Id, "nationalId");
        var passport = await ReadAsync(_personId, created.Record.Id, "passport");
        Assert.NotNull(nationalId);
        Assert.NotNull(passport);
        Assert.NotEqual(nationalId, passport);
        Assert.Equal(Hash(1, _personId, "nationalId", "same"), nationalId);
        Assert.Equal(Hash(1, _personId, "passport", "same"), passport);
    }

    [Fact]
    public async Task A_child_row_stores_the_lookup_hash_of_its_exact_lookup_field()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;

        var created = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Ann", "cards": [{ "number": "4111" }, { "number": null }] } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var stored = await ReadChildAsync(created.Record!.Id);
        Assert.Equal([Hash(1, _cardId, "number", "4111"), null], stored);
    }

    [Fact]
    public async Task A_tenant_without_a_lookup_key_refuses_a_value_and_writes_nothing()
    {
        var cipher = Cipher();
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;

        var exception = await Assert.ThrowsAsync<TenantDataKeyMissingException>(
            () => CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Ann", "nationalId": "079-123" } }"""));

        Assert.Equal(TenantKeyKind.Lookup, exception.Kind);
        await using var count = new NpgsqlCommand($"SELECT count(*) FROM {RecordQueries.Table(person)}", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync(CancellationToken))!);
    }

    // The tenant holds data key 1 and a random lookup key for each number in lookupKeys, none when it is empty.
    private RecordCipher Cipher(params int[] lookupKeys)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"Tenants:{_tenant}:DataKeys:1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        foreach (var number in lookupKeys)
        {
            _lookupKeys[number] = RandomNumberGenerator.GetBytes(32);
            settings[$"Tenants:{_tenant}:LookupKeys:{number}"] = Convert.ToBase64String(_lookupKeys[number]);
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new RecordCipher(new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration))), _tenant);
    }

    // The number of the lookup key, 4 bytes big-endian, then the HMAC-SHA256 of the message.
    private byte[] Hash(int number, Guid entityId, string field, string text)
    {
        var message = Encoding.UTF8.GetBytes($"{_tenant}\0{entityId:D}\0{field.ToLowerInvariant()}\0{text}");
        return [.. new byte[] { 0, 0, 0, (byte)number }, .. HMACSHA256.HashData(_lookupKeys[number], message)];
    }

    private async Task<(ReleaseCompilationResult Compiled, Axis.Configuration.Model.EntityModel Person, NpgsqlConnection Connection)> ProvisionAsync()
    {
        using var folder = Folder();
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        }

        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using (var configuration = DataDatabaseFixture.CreateConfigurationContext(connection))
        await using (var data = DataDatabaseFixture.CreateContext(connection))
        {
            var result = await ReleaseActivator.ActivateAsync(
                compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(_tenant), OpenInstanceCounter.Instance, 10, CancellationToken);
            Assert.Empty(result.Diagnostics);
        }

        Assert.True(compiled.Model!.TryGetEntity("Person", out var person));
        return (compiled, person, connection);
    }

    private Task<RecordCommandResult> CreateAsync(
        NpgsqlConnection connection, ReleaseCompilationResult compiled, Axis.Configuration.Model.EntityModel person, RecordCipher cipher, string body) =>
        RecordCommand.CreateAsync(
            connection,
            _tenant,
            compiled.Model!,
            compiled.Release!.Id,
            person,
            new RecordActor("requester", ["clerk"]),
            Body(body),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);

    private Task<RecordCommandResult> UpdateAsync(
        NpgsqlConnection connection, ReleaseCompilationResult compiled, Axis.Configuration.Model.EntityModel person, RecordCipher cipher, Guid id, string body) =>
        RecordCommand.UpdateAsync(
            connection,
            _tenant,
            compiled.Model!,
            compiled.Release!.Id,
            person,
            id,
            new RecordActor("requester", ["clerk"]),
            Body(body),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);

    private async Task<byte[]?> ReadAsync(Guid entityId, Guid id, string field)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"SELECT {EntityNaming.Quote(EntityNaming.LookupColumn(field))} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(CancellationToken) as byte[];
    }

    private async Task<List<byte[]?>> ReadChildAsync(Guid ownerId)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"SELECT {EntityNaming.Quote(EntityNaming.LookupColumn("number"))} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_cardId))} WHERE owner_id = @owner ORDER BY position");
        command.Parameters.AddWithValue("owner", ownerId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<byte[]?>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0));
        }

        return rows;
    }

    private static RecordBodyReader Body(string json) => _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(json));

    private TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With(
                "entities/person.json",
                $$"""
                { "id": "{{_personId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "fields": [
                    { "name": "name", "type": "text" },
                    { "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true },
                    { "name": "passport", "type": "text", "sensitive": true, "exactLookup": true },
                    { "name": "cards", "type": "child-collection", "target": "Card" } ] }
                """)
            .With(
                "entities/card.json",
                $$"""
                { "id": "{{_cardId}}", "kind": "entity", "name": "Card", "formatVersion": 1, "fields": [
                    { "name": "number", "type": "text", "sensitive": true, "exactLookup": true } ] }
                """)
            .With("roles/clerk.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }""")
            .With(
                "texts/en.json",
                $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "role.clerk": "Clerk" } }""")
            .With(
                "policies/clerk-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "ClerkAccess", "formatVersion": 1, "roles": ["clerk"], "grants": [
                    { "name": "personRead", "entity": "Person", "actions": ["read", "create", "update"] }] }
                """);
}
