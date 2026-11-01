using System.Buffers.Binary;
using Axis.Configuration.Tests;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>tenant data-key rotate</c> moves every stored value and lookup hash of a tenant to a new data key and lookup key, and
/// <c>tenant data-key retire</c> deletes the older keys once nothing names them. Both write one platform audit record that names
/// key numbers and never a key.
/// </summary>
public sealed class AdminTenantDataKeyTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static readonly Guid _personId = Guid.NewGuid();
    private static readonly Guid _cardId = Guid.NewGuid();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rotate_moves_every_value_and_hash_to_the_new_keys_and_retire_then_deletes_the_old_ones()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await ActivatedTenantAsync(scenario);
        await using var connection = await SchemaConnectionAsync(scenario, tenant);
        var stale = NewValues(scenario);
        await PrimeAsync(stale, tenant);
        var people = new[]
        {
            await InsertPersonAsync(connection, stale, tenant, "079-123", "P-1", ["4111", "4222"]),
            await InsertPersonAsync(connection, stale, tenant, "079-456", "P-2", []),
            await InsertPersonAsync(connection, stale, tenant, "079-789", "P-3", []),
        };
        Assert.All(await ReadEncryptedAsync(connection), column => Assert.Equal(1, column.Number));
        var oldKeys = await KeyTextsAsync(scenario, tenant);

        var rotated = await scenario.RunAsync(["tenant", "data-key", "rotate", tenant], extra: AdminScenario.Directory);

        // Three people hold two values and a hash each, and the two cards hold a value and a hash each.
        Assert.Equal(0, rotated.Exit);
        Assert.Equal([$"{tenant} data-key rotate ok: data key 2, lookup key 2, 13 values updated"], rotated.Output);
        Assert.Empty(rotated.Error);
        Assert.Equal(13, (await ReadEncryptedAsync(connection)).Count);
        Assert.All(await ReadEncryptedAsync(connection), column => Assert.Equal(2, column.Number));

        // The service that read the keys before the rotation reads the new values, by asking for their number.
        foreach (var person in people)
        {
            Assert.Equal(person.NationalId, await stale.DecryptTextAsync(Scope(tenant, _personId, person.Id), "nationalId", await ReadValueAsync(connection, _personId, "nationalId", person.Id), CancellationToken));
            Assert.Equal(person.Passport, await stale.DecryptTextAsync(Scope(tenant, _personId, person.Id), "passport", await ReadValueAsync(connection, _personId, "passport", person.Id), CancellationToken));
        }

        // A search value hashed under the newest lookup key finds the same record.
        var fresh = NewValues(scenario);
        foreach (var person in people)
        {
            var hash = await fresh.LookupTextAsync(tenant, _personId, "nationalId", person.NationalId, CancellationToken);
            Assert.Equal([person.Id], await FindAsync(connection, _personId, "nationalId", hash));
        }

        Assert.Equal("4111", await stale.DecryptTextAsync(Scope(tenant, _cardId, people[0].Cards[0]), "number", await ReadChildValueAsync(connection, people[0].Cards[0]), CancellationToken));
        Assert.Equal([people[0].Cards[1]], await FindAsync(connection, _cardId, "number", await fresh.LookupTextAsync(tenant, _cardId, "number", "4222", CancellationToken)));

        await scenario.AssertNoSecretsAsync();
        var retired = await scenario.RunAsync(["tenant", "data-key", "retire", tenant], extra: AdminScenario.Directory);

        Assert.Equal(0, retired.Exit);
        Assert.Equal([$"{tenant} data-key retire ok: 1 data keys, 1 lookup keys deleted"], retired.Output);
        Assert.Equal([(TenantKeyKinds.Data, 2), (TenantKeyKinds.Lookup, 2)], (await scenario.KeysAsync(tenant)).Select(key => (key.Kind, key.Number)));
        var records = (await scenario.AuditRecordsAsync()).Where(record => record.Command.StartsWith("tenant data-key", StringComparison.Ordinal)).ToList();
        Assert.Equal(["tenant data-key rotate", "tenant data-key retire"], records.Select(record => record.Command));
        Assert.All(records, record => Assert.Equal(tenant, record.TenantId));
        Assert.Equal(["data_key:2", "lookup_key:2"], records[0].Changed);
        Assert.Equal(["data_key:1", "lookup_key:1"], records[1].Changed);
        var texts = rotated.Output.Concat(rotated.Error).Concat(retired.Output).Concat(retired.Error).Concat(records.Select(record => record.Text));
        Assert.All(texts, text => Assert.All(oldKeys, key => Assert.DoesNotContain(key, text, StringComparison.Ordinal)));

        // The rewritten values still read after the old keys are gone.
        Assert.Equal(people[1].NationalId, await NewValues(scenario).DecryptTextAsync(Scope(tenant, _personId, people[1].Id), "nationalId", await ReadValueAsync(connection, _personId, "nationalId", people[1].Id), CancellationToken));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task A_value_written_with_the_old_key_after_the_rotation_makes_retire_refuse_until_a_second_rotate()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await ActivatedTenantAsync(scenario);
        await using var connection = await SchemaConnectionAsync(scenario, tenant);
        var stale = NewValues(scenario);
        await PrimeAsync(stale, tenant);
        await InsertPersonAsync(connection, stale, tenant, "079-123", "P-1", []);
        Assert.Equal(0, (await scenario.RunAsync(["tenant", "data-key", "rotate", tenant], extra: AdminScenario.Directory)).Exit);

        // The host still holds key 1 as its newest, so it writes with it.
        var late = await InsertPersonAsync(connection, stale, tenant, "079-999", "P-9", []);
        Assert.Contains(await ReadEncryptedAsync(connection), column => column.Number == 1);

        var refused = await scenario.RunAsync(["tenant", "data-key", "retire", tenant], extra: AdminScenario.Directory);

        Assert.Equal(1, refused.Exit);
        Assert.Equal([$"{tenant} data-key retire refused: 3 stored values still name an older key. Run tenant data-key rotate again."], refused.Output);
        Assert.Equal(4, (await scenario.KeysAsync(tenant)).Count);
        Assert.DoesNotContain(await scenario.AuditRecordsAsync(), record => record.Command == "tenant data-key retire");

        var again = await scenario.RunAsync(["tenant", "data-key", "rotate", tenant], extra: AdminScenario.Directory);

        Assert.Equal(0, again.Exit);
        Assert.Equal([$"{tenant} data-key rotate ok: data key 3, lookup key 3, 6 values updated"], again.Output);
        Assert.Equal("P-9", await NewValues(scenario).DecryptTextAsync(Scope(tenant, _personId, late.Id), "passport", await ReadValueAsync(connection, _personId, "passport", late.Id), CancellationToken));
        var retired = await scenario.RunAsync(["tenant", "data-key", "retire", tenant], extra: AdminScenario.Directory);

        Assert.Equal(0, retired.Exit);
        Assert.Equal([$"{tenant} data-key retire ok: 2 data keys, 2 lookup keys deleted"], retired.Output);
        Assert.Equal([(TenantKeyKinds.Data, 3), (TenantKeyKinds.Lookup, 3)], (await scenario.KeysAsync(tenant)).Select(key => (key.Kind, key.Number)));
        await scenario.AssertNoSecretsAsync();
    }

    [Fact]
    public async Task Retire_with_one_key_of_each_kind_deletes_nothing_and_writes_no_record()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await ActivatedTenantAsync(scenario);

        var retired = await scenario.RunAsync(["tenant", "data-key", "retire", tenant], extra: AdminScenario.Directory);

        Assert.Equal(0, retired.Exit);
        Assert.Equal([$"{tenant} data-key retire ok: 0 data keys, 0 lookup keys deleted"], retired.Output);
        Assert.Equal(2, (await scenario.KeysAsync(tenant)).Count);
        Assert.DoesNotContain(await scenario.AuditRecordsAsync(), record => record.Command.StartsWith("tenant data-key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_usage_error_the_configuration_source_an_unknown_or_provisioning_tenant_exit_2_and_change_nothing()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        var provisioning = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(provisioning, $"{provisioning}.example.test")).Exit);
        await scenario.SetStatusAsync(provisioning, "provisioning");
        var before = await scenario.SnapshotAsync();
        var keys = (await scenario.KeysAsync(tenant)).Select(key => (key.Kind, key.Number)).ToList();
        var records = (await scenario.AuditRecordsAsync()).Count;

        foreach (var verb in new[] { "rotate", "retire" })
        {
            var command = new[] { "tenant", "data-key", verb };
            Assert.Equal(2, (await scenario.RunAsync(command, extra: AdminScenario.Directory)).Exit);
            Assert.Equal(2, (await scenario.RunAsync([.. command, tenant, "extra"], extra: AdminScenario.Directory)).Exit);
            Assert.Equal(2, (await scenario.RunAsync([.. command, "--tenant"], extra: AdminScenario.Directory)).Exit);

            var configuration = await scenario.RunAsync([.. command, tenant]);
            Assert.Equal(2, configuration.Exit);
            Assert.Equal(["The tenant data-key commands need Tenancy:Source=directory."], configuration.Error);

            var unknown = await scenario.RunAsync([.. command, "no-such"], extra: AdminScenario.Directory);
            Assert.Equal(2, unknown.Exit);
            Assert.Equal(["The tenant does not exist."], unknown.Error);

            var waiting = await scenario.RunAsync([.. command, provisioning], extra: AdminScenario.Directory);
            Assert.Equal(2, waiting.Exit);
            Assert.Equal([$"The tenant '{provisioning}' is still provisioning. Finish it with tenant create."], waiting.Error);
        }

        Assert.Equal(before, await scenario.SnapshotAsync());
        Assert.Equal(keys, (await scenario.KeysAsync(tenant)).Select(key => (key.Kind, key.Number)));
        Assert.Equal(records, (await scenario.AuditRecordsAsync()).Count);
    }

    private sealed record Person(Guid Id, string NationalId, string Passport, Guid[] Cards);

    private static async Task<string> ActivatedTenantAsync(AdminScenario scenario)
    {
        var tenant = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(tenant, $"{tenant}.example.test")).Exit);
        using var folder = Folder();
        var activated = await scenario.RunAsync(["activate", folder.Path, "--tenant", tenant]);
        Assert.Equal(0, activated.Exit);
        return tenant;
    }

    private static async Task<NpgsqlConnection> SchemaConnectionAsync(AdminScenario scenario, string tenant)
    {
        var row = await scenario.RowAsync(tenant);
        var password = AdminScenario.Cipher.Decrypt(tenant, TenantSecretFields.SchemaPassword, row.SchemaPassword).Reveal();
        var connection = new NpgsqlConnection(scenario.TenantConnectionString(tenant, row.SchemaUser, password));
        await connection.OpenAsync(CancellationToken);
        return connection;
    }

    // A key service as a running host has it: it reads the keys when first asked, and not again until a value names a number it lacks.
    private static SensitiveValues NewValues(AdminScenario scenario) =>
        new(new TenantKeys(new DirectoryTenantKeySource(scenario.Platform, AdminScenario.Cipher)));

    private static async Task PrimeAsync(SensitiveValues values, string tenant) =>
        Assert.NotNull(await values.EncryptTextAsync(Scope(tenant, _personId, Guid.NewGuid()), "name", "x", CancellationToken));

    private static SensitiveValueScope Scope(string tenant, Guid entityId, Guid id) => new(tenant, entityId, id);

    // The base64 text of each key of the tenant.
    private static async Task<List<string>> KeyTextsAsync(AdminScenario scenario, string tenant) =>
        [.. (await scenario.KeysAsync(tenant)).Select(key => AdminScenario.Cipher
            .Decrypt(tenant, key.Kind == TenantKeyKinds.Data ? TenantSecretFields.DataKey(key.Number) : TenantSecretFields.LookupKey(key.Number), key.Key)
            .Reveal())];

    private static async Task<Person> InsertPersonAsync(
        NpgsqlConnection connection, SensitiveValues values, string tenant, string nationalId, string passport, string[] cards)
    {
        var id = Guid.NewGuid();
        await using (var command = new NpgsqlCommand(
            $"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_personId))} (id, {Quote("f_name")}, {Quote("f_nationalid")}, {Quote("f_nationalid_lookup")}, {Quote("f_passport")}) "
            + "VALUES (@id, 'Ann', @national, @lookup, @passport)",
            connection))
        {
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("national", await values.EncryptTextAsync(Scope(tenant, _personId, id), "nationalId", nationalId, CancellationToken));
            command.Parameters.AddWithValue("lookup", await values.LookupTextAsync(tenant, _personId, "nationalId", nationalId, CancellationToken));
            command.Parameters.AddWithValue("passport", await values.EncryptTextAsync(Scope(tenant, _personId, id), "passport", passport, CancellationToken));
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var cardIds = new List<Guid>();
        for (var position = 0; position < cards.Length; position++)
        {
            var cardId = Guid.NewGuid();
            cardIds.Add(cardId);
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_cardId))} (id, owner_id, position, {Quote("f_number")}, {Quote("f_number_lookup")}) "
                + "VALUES (@id, @owner, @position, @number, @lookup)",
                connection);
            command.Parameters.AddWithValue("id", cardId);
            command.Parameters.AddWithValue("owner", id);
            command.Parameters.AddWithValue("position", position);
            command.Parameters.AddWithValue("number", await values.EncryptTextAsync(Scope(tenant, _cardId, cardId), "number", cards[position], CancellationToken));
            command.Parameters.AddWithValue("lookup", await values.LookupTextAsync(tenant, _cardId, "number", cards[position], CancellationToken));
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return new Person(id, nationalId, passport, [.. cardIds]);
    }

    // The key number at the start of every stored value and lookup hash of the sensitive columns.
    private static async Task<List<(string Column, int Number)>> ReadEncryptedAsync(NpgsqlConnection connection)
    {
        var numbers = new List<(string, int)>();
        foreach (var (entity, columns) in new (Guid, string[])[] { (_personId, ["f_nationalid", "f_nationalid_lookup", "f_passport"]), (_cardId, ["f_number", "f_number_lookup"]) })
        {
            foreach (var column in columns)
            {
                await using var command = new NpgsqlCommand($"SELECT {Quote(column)} FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity))}", connection);
                await using var reader = await command.ExecuteReaderAsync(CancellationToken);
                while (await reader.ReadAsync(CancellationToken))
                {
                    numbers.Add((column, BinaryPrimitives.ReadInt32BigEndian(reader.GetFieldValue<byte[]>(0))));
                }
            }
        }

        return numbers;
    }

    private static Task<byte[]> ReadValueAsync(NpgsqlConnection connection, Guid entity, string field, Guid id) =>
        ReadBytesAsync(connection, EntityNaming.Table(entity), EntityNaming.Column(field), id);

    private static Task<byte[]> ReadChildValueAsync(NpgsqlConnection connection, Guid id) =>
        ReadBytesAsync(connection, EntityNaming.Table(_cardId), EntityNaming.Column("number"), id);

    private static async Task<byte[]> ReadBytesAsync(NpgsqlConnection connection, string table, string column, Guid id)
    {
        await using var command = new NpgsqlCommand($"SELECT {Quote(column)} FROM {EntityNaming.QualifiedTable(table)} WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (byte[])(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static async Task<List<Guid>> FindAsync(NpgsqlConnection connection, Guid entity, string field, byte[] hash)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT id FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entity))} WHERE {Quote(EntityNaming.LookupColumn(field))} = @hash",
            connection);
        command.Parameters.AddWithValue("hash", hash);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static string Quote(string identifier) => EntityNaming.Quote(identifier);

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "Keys{{Guid.NewGuid():N}}", "formatVersion": 1 }""")
            .With(
                "entities/person.json",
                $$"""
                { "id": "{{_personId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "fields": [
                    { "name": "name", "type": "text" },
                    { "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true },
                    { "name": "passport", "type": "text", "sensitive": true },
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
