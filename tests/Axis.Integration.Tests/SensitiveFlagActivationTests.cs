using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A release that adds or removes <c>sensitive</c> on a field that already has a column makes activation encrypt or decrypt its
/// stored values in its own transaction, and a <c>renameField</c> of a sensitive field encrypts them again under the new name.
/// A value that does not decrypt, or a missing data key, stops the activation and leaves the table and the active release alone.
/// </summary>
public sealed class SensitiveFlagActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string Note = "Secret note of Ngô Thị Hạnh";
    private const string PlainNote = """{ "name": "note", "type": "text" }""";
    private const string SensitiveNote = """{ "name": "note", "type": "text", "sensitive": true }""";
    private const string LookupNote = """{ "name": "note", "type": "text", "sensitive": true, "exactLookup": true }""";
    private const string PlainAmount = """{ "name": "amount", "type": "decimal", "precision": 12, "scale": 2 }""";
    private const string SensitiveAmount = """{ "name": "amount", "type": "decimal", "precision": 12, "scale": 2, "sensitive": true }""";
    private const string PlainDue = """{ "name": "due", "type": "date" }""";
    private const string SensitiveDue = """{ "name": "due", "type": "date", "sensitive": true }""";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";
    private readonly string _dataKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private SensitiveValues Values(bool withKey = true, bool withLookupKey = true)
    {
        var settings = new Dictionary<string, string?>();
        if (withKey)
        {
            settings[$"Tenants:{_tenant}:DataKeys:1"] = _dataKey;
        }

        if (withLookupKey)
        {
            settings[$"Tenants:{_tenant}:LookupKeys:1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration)));
    }

    [Fact]
    public async Task Marking_fields_sensitive_encrypts_their_values_and_removing_the_mark_restores_plain_columns_with_the_same_values()
    {
        var values = Values();
        var cipher = new RecordCipher(values, _tenant);
        using var folder = Application(PlainNote, PlainAmount, PlainDue);
        var first = await CompileAsync(folder);
        await ActivateAsync(first, values);
        var filled = await InsertPlainAsync("1250.5", "2026-11-02", Note);
        var partial = await InsertPlainAsync(null, null, null);
        var before = await ReadAsync(first, filled, null);
        var emptyBefore = await ReadAsync(first, partial, null);
        Assert.Equal(Note, before["note"]!.GetValue<string>());

        folder.With("entities/order.json", Order(SensitiveNote, SensitiveAmount, SensitiveDue));
        var second = await CompileAsync(folder);
        await ActivateAsync(second, values);

        Assert.Equal(["bytea", "bytea", "bytea"], await ColumnTypesAsync());
        Assert.Equal(second.Release!.Id, await ActiveReleaseIdAsync());
        foreach (var column in new[] { "f_note", "f_amount", "f_due" })
        {
            var stored = await StoredBytesAsync(column, filled);
            Assert.NotNull(stored);
            Assert.True(stored.Length > 4 + 12 + 16);
            Assert.Null(await StoredBytesAsync(column, partial));
        }

        var noteBytes = (await StoredBytesAsync("f_note", filled))!;
        Assert.False(noteBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Note)) >= 0);
        Assert.True(JsonNode.DeepEquals(before, await ReadAsync(second, filled, cipher)));
        Assert.True(JsonNode.DeepEquals(emptyBefore, await ReadAsync(second, partial, cipher)));

        folder.With("entities/order.json", Order(PlainNote, PlainAmount, PlainDue));
        var third = await CompileAsync(folder);
        await ActivateAsync(third, values);

        Assert.Equal(["text", "numeric(12,2)", "date"], await ColumnTypesAsync());
        Assert.Equal(third.Release!.Id, await ActiveReleaseIdAsync());
        Assert.Equal([Note, "1250.50", "2026-11-02"], await PlainValuesAsync(filled));
        Assert.Equal([null, null, null], await PlainValuesAsync(partial));
        Assert.True(JsonNode.DeepEquals(before, await ReadAsync(third, filled, null)));
        Assert.True(JsonNode.DeepEquals(emptyBefore, await ReadAsync(third, partial, null)));
    }

    [Fact]
    public async Task A_rename_of_a_sensitive_field_keeps_every_value_readable_under_the_new_name()
    {
        var values = Values();
        var cipher = new RecordCipher(values, _tenant);
        using var folder = Application(SensitiveNote);
        var first = await CompileAsync(folder);
        await ActivateAsync(first, values);
        var rows = new List<(Guid Id, string Text)>();
        for (var index = 0; index < 3; index++)
        {
            var row = (Id: Guid.NewGuid(), Text: $"{Note} {index}");
            rows.Add(row);
            await InsertStoredAsync("f_note", row.Id, await EncryptAsync(values, row.Id, "note", row.Text));
        }

        var empty = Guid.NewGuid();
        await InsertStoredAsync("f_note", empty, null);
        var old = await StoredBytesAsync("f_note", rows[0].Id);
        folder
            .With("entities/order.json", Order("""{ "name": "remark", "type": "text", "sensitive": true }"""))
            .With("migrations/0001-rename-note.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "RenameNote", "formatVersion": 1, "entity": "Order", "changes": [{ "type": "renameField", "from": "note", "to": "remark" }] }
                """);
        var second = await CompileAsync(folder);

        await ActivateAsync(second, values);

        Assert.Equal(["bytea"], await ColumnTypesAsync());
        Assert.Equal(second.Release!.Id, await ActiveReleaseIdAsync());
        Assert.NotEqual(Convert.ToHexString(old!), Convert.ToHexString((await StoredBytesAsync("f_remark", rows[0].Id))!));
        foreach (var (id, text) in rows)
        {
            Assert.Equal(text, (await ReadAsync(second, id, cipher))["remark"]!.GetValue<string>());
        }

        Assert.Null((await ReadAsync(second, empty, cipher))["remark"]);
    }

    [Fact]
    public async Task A_value_that_does_not_decrypt_fails_the_activation_that_removes_the_mark_and_changes_nothing()
    {
        var values = Values();
        using var folder = Application(PlainNote);
        await ActivateAsync(await CompileAsync(folder), values);
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            ids.Add(await InsertPlainAsync(null, null, $"{Note} {index}"));
        }

        folder.With("entities/order.json", Order(SensitiveNote));
        var marked = await CompileAsync(folder);
        await ActivateAsync(marked, values);
        await ExecuteAsync(
            $"UPDATE {Table()} SET f_note = set_byte(f_note, 20, get_byte(f_note, 20) # 1) WHERE id = '{ids[1]}'");
        var bytes = await AllStoredAsync();
        folder.With("entities/order.json", Order(PlainNote));
        var unmarked = await CompileAsync(folder);

        var failure = await Assert.ThrowsAsync<SensitiveValueDecryptionException>(() => ActivateAsync(unmarked, values));

        Assert.DoesNotContain(Note, failure.Message, StringComparison.Ordinal);
        Assert.Equal(["bytea"], await ColumnTypesAsync());
        Assert.Equal(bytes, await AllStoredAsync());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        var active = await new ActiveReleaseStore(configuration).FindByApplicationIdAsync(_applicationId, CancellationToken);
        Assert.Equal(marked.Release!.Id, active?.ReleaseId);
        Assert.Equal(marked.Release.Id, await ActiveReleaseIdAsync());
    }

    [Fact]
    public async Task Activation_without_the_data_keys_fails_closed_for_a_flag_change_and_for_a_sensitive_rename()
    {
        var values = Values();
        using var folder = Application(PlainNote);
        var first = await CompileAsync(folder);
        await ActivateAsync(first, values);
        await InsertPlainAsync(null, null, Note);
        folder.With("entities/order.json", Order(SensitiveNote));
        var marked = await CompileAsync(folder);

        var withoutValues = await Assert.ThrowsAsync<InvalidOperationException>(() => ActivateAsync(marked, null));
        var withoutKey = await Assert.ThrowsAsync<TenantDataKeyMissingException>(() => ActivateAsync(marked, Values(withKey: false)));

        Assert.Equal(RecordCipher.MissingMessage, withoutValues.Message);
        Assert.Equal(_tenant, withoutKey.TenantId);
        Assert.Equal(["text"], await ColumnTypesAsync());
        Assert.Equal([Note], await ReadScalarsAsync($"SELECT f_note FROM {Table()}"));
        Assert.Equal(first.Release!.Id, await ActiveReleaseIdAsync());

        await ActivateAsync(marked, values);
        folder
            .With("entities/order.json", Order("""{ "name": "remark", "type": "text", "sensitive": true }"""))
            .With("migrations/0001-rename-note.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "RenameNote", "formatVersion": 1, "entity": "Order", "changes": [{ "type": "renameField", "from": "note", "to": "remark" }] }
                """);
        var renamed = await CompileAsync(folder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ActivateAsync(renamed, null));

        Assert.Equal(["f_note"], await ReadScalarsAsync($"SELECT column_name FROM information_schema.columns WHERE table_schema = 'entities' AND table_name = '{EntityNaming.Table(_orderId)}' AND column_name LIKE 'f\\_%'"));
        Assert.Equal(marked.Release!.Id, await ActiveReleaseIdAsync());
    }

    [Fact]
    public async Task Adding_exact_lookup_fills_a_hash_for_every_row_and_removing_it_drops_the_column()
    {
        var values = Values();
        using var folder = Application(SensitiveNote);
        await ActivateAsync(await CompileAsync(folder), values);
        var rows = await InsertEncryptedRowsAsync(values, "note");
        var empty = Guid.NewGuid();
        await InsertStoredAsync("f_note", empty, null);

        folder.With("entities/order.json", Order(LookupNote));
        var gained = await CompileAsync(folder);
        await ActivateAsync(gained, values);

        Assert.Equal(gained.Release!.Id, await ActiveReleaseIdAsync());
        foreach (var (id, text) in rows)
        {
            Assert.Equal(
                Convert.ToHexString(await values.LookupTextAsync(_tenant, _orderId, "note", text, CancellationToken)),
                Convert.ToHexString((await StoredBytesAsync("f_note_lookup", id))!));
        }

        Assert.Null(await StoredBytesAsync("f_note_lookup", empty));

        folder.With("entities/order.json", Order(SensitiveNote));
        var lost = await CompileAsync(folder);
        await ActivateAsync(lost, values);

        Assert.Equal(lost.Release!.Id, await ActiveReleaseIdAsync());
        Assert.Equal(["f_note"], await ColumnNamesAsync());
    }

    [Fact]
    public async Task A_field_that_becomes_sensitive_and_exact_lookup_in_one_release_is_encrypted_and_hashed()
    {
        var values = Values();
        var cipher = new RecordCipher(values, _tenant);
        using var folder = Application(PlainNote);
        await ActivateAsync(await CompileAsync(folder), values);
        var first = await InsertPlainAsync(null, null, Note);
        var empty = await InsertPlainAsync(null, null, null);

        folder.With("entities/order.json", Order(LookupNote));
        var second = await CompileAsync(folder);
        await ActivateAsync(second, values);

        Assert.Equal(Note, (await ReadAsync(second, first, cipher))["note"]!.GetValue<string>());
        Assert.Equal(
            Convert.ToHexString(await values.LookupTextAsync(_tenant, _orderId, "note", Note, CancellationToken)),
            Convert.ToHexString((await StoredBytesAsync("f_note_lookup", first))!));
        Assert.Null(await StoredBytesAsync("f_note_lookup", empty));
    }

    [Fact]
    public async Task A_rename_of_an_exact_lookup_field_hashes_each_value_under_the_new_name()
    {
        var values = Values();
        using var folder = Application(LookupNote);
        await ActivateAsync(await CompileAsync(folder), values);
        var rows = new List<(Guid Id, string Text)>();
        for (var index = 0; index < 3; index++)
        {
            var row = (Id: Guid.NewGuid(), Text: $"{Note} {index}");
            rows.Add(row);
            await InsertStoredAsync("f_note", row.Id, await EncryptAsync(values, row.Id, "note", row.Text));
            await ExecuteAsync(
                $"UPDATE {Table()} SET f_note_lookup = decode('{Convert.ToHexString(await values.LookupTextAsync(_tenant, _orderId, "note", row.Text, CancellationToken))}', 'hex') WHERE id = '{row.Id}'");
        }

        folder
            .With("entities/order.json", Order("""{ "name": "remark", "type": "text", "sensitive": true, "exactLookup": true }"""))
            .With("migrations/0001-rename-note.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "migration", "name": "RenameNote", "formatVersion": 1, "entity": "Order", "changes": [{ "type": "renameField", "from": "note", "to": "remark" }] }
                """);
        var second = await CompileAsync(folder);

        await ActivateAsync(second, values);

        Assert.Equal(["f_remark", "f_remark_lookup"], (await ColumnNamesAsync()).Order().ToList());
        foreach (var (id, text) in rows)
        {
            var stored = Convert.ToHexString((await StoredBytesAsync("f_remark_lookup", id))!);
            Assert.Equal(Convert.ToHexString(await values.LookupTextAsync(_tenant, _orderId, "remark", text, CancellationToken)), stored);
            Assert.NotEqual(Convert.ToHexString(await values.LookupTextAsync(_tenant, _orderId, "note", text, CancellationToken)), stored);
        }
    }

    [Fact]
    public async Task Activation_without_a_lookup_key_fails_closed_when_exact_lookup_is_added()
    {
        var values = Values();
        using var folder = Application(SensitiveNote);
        var first = await CompileAsync(folder);
        await ActivateAsync(first, values);
        await InsertEncryptedRowsAsync(values, "note");
        folder.With("entities/order.json", Order(LookupNote));
        var gained = await CompileAsync(folder);

        var failure = await Assert.ThrowsAsync<TenantDataKeyMissingException>(() => ActivateAsync(gained, Values(withLookupKey: false)));

        Assert.Equal(TenantKeyKind.Lookup, failure.Kind);
        Assert.Equal(["f_note"], await ColumnNamesAsync());
        Assert.Equal(first.Release!.Id, await ActiveReleaseIdAsync());
    }

    private async Task<List<(Guid Id, string Text)>> InsertEncryptedRowsAsync(SensitiveValues values, string field)
    {
        var rows = new List<(Guid Id, string Text)>();
        for (var index = 0; index < 3; index++)
        {
            var row = (Id: Guid.NewGuid(), Text: $"{Note} {index}");
            rows.Add(row);
            await InsertStoredAsync($"f_{field}", row.Id, await EncryptAsync(values, row.Id, field, row.Text));
        }

        return rows;
    }

    private string Table() => EntityNaming.QualifiedTable(EntityNaming.Table(_orderId));

    private string Order(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private TemporaryFolder Application(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With("entities/order.json", Order(fields));

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        return compiled;
    }

    private async Task ActivateAsync(ReleaseCompilationResult compiled, SensitiveValues? values)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var result = await ReleaseActivator.ActivateAsync(
            compiled,
            new ActiveReleaseStore(configuration),
            data,
            ActivationBindings.None(_tenant) with { Sensitive = values },
            OpenInstanceCounter.Instance,
            CancellationToken);
        Assert.Empty(result.Diagnostics);
    }

    private async Task<JsonNode> ReadAsync(ReleaseCompilationResult compiled, Guid id, RecordCipher? cipher)
    {
        Assert.True(compiled.Model!.TryGetEntity("Order", out var entity));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var record = await RecordQueries.GetAsync(connection, compiled.Model, entity, id, cipher, CancellationToken);
        Assert.NotNull(record);
        return new JsonObject(record.Values.Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())));
    }

    private async Task<byte[]> EncryptAsync(SensitiveValues values, Guid id, string field, string text) =>
        await values.EncryptTextAsync(new SensitiveValueScope(_tenant, _orderId, id), field, text, CancellationToken);

    private async Task<Guid> InsertPlainAsync(string? amount, string? due, string? note)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var columns = await ColumnNamesAsync();
        var sql = $"INSERT INTO {Table()} (id{(columns.Contains("f_note") ? ", f_note" : "")}{(columns.Contains("f_amount") ? ", f_amount" : "")}{(columns.Contains("f_due") ? ", f_due" : "")}) "
            + $"VALUES (@id{(columns.Contains("f_note") ? ", @note" : "")}{(columns.Contains("f_amount") ? ", @amount::numeric" : "")}{(columns.Contains("f_due") ? ", @due::date" : "")})";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("amount", (object?)amount ?? DBNull.Value);
        command.Parameters.AddWithValue("due", (object?)due ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task InsertStoredAsync(string column, Guid id, byte[]? stored)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"INSERT INTO {Table()} (id, {EntityNaming.Quote(column)}) VALUES (@id, @stored)");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("stored", NpgsqlTypes.NpgsqlDbType.Bytea, (object?)stored ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<byte[]?> StoredBytesAsync(string column, Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"SELECT {EntityNaming.Quote(column)} FROM {Table()} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(CancellationToken) as byte[];
    }

    private Task<List<string>> AllStoredAsync() =>
        ReadScalarsAsync($"SELECT id::text || ':' || coalesce(encode(f_note, 'hex'), '') FROM {Table()} ORDER BY id");

    private async Task<List<string?>> PlainValuesAsync(Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"SELECT f_note, f_amount::text, f_due::text FROM {Table()} WHERE id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return [.. Enumerable.Range(0, 3).Select(index => reader.IsDBNull(index) ? null : reader.GetString(index))];
    }

    private Task<List<string>> ColumnTypesAsync() =>
        ReadScalarsAsync(
            $"SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = '{Table()}'::regclass AND attname LIKE 'f\\_%' AND NOT attisdropped ORDER BY attnum");

    private async Task<HashSet<string>> ColumnNamesAsync() =>
        [.. await ReadScalarsAsync($"SELECT attname FROM pg_attribute WHERE attrelid = '{Table()}'::regclass AND attname LIKE 'f\\_%' AND NOT attisdropped")];

    private async Task<Guid?> ActiveReleaseIdAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @id");
        command.Parameters.AddWithValue("id", _applicationId);
        return await command.ExecuteScalarAsync(CancellationToken) as Guid?;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<List<string>> ReadScalarsAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var results = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
