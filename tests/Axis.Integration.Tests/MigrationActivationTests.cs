using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Data.Schema;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class MigrationActivationTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application, entity and migration ids and names, so tests sharing the
    // database do not see each other's tables, records and active releases.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _invoiceId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();
    private readonly Guid _migrationId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";
    private const string NoteField = """{ "name": "note", "type": "text" }""";
    private const string RemarkField = """{ "name": "remark", "type": "text" }""";
    private const string StatusField = """{ "name": "status", "type": "enum", "values": ["draft", "done"] }""";
    private const string StateField = """{ "name": "state", "type": "enum", "values": ["draft", "done"] }""";
    private const string PriorityField = """{ "name": "priority", "type": "integer" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_pending_rename_keeps_every_stored_value_under_the_new_field_name_and_the_record_api_reads_it()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField, StatusField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var first = await InsertOrderAsync("PO-1", "first note");
        var second = await InsertOrderAsync("PO-2", null);
        folder
            .With("entities/order.json", Order(NumberField, RemarkField, StateField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"))
            .With("migrations/0002-rename-status.json", RenameMigration(Guid.NewGuid(), "status", "state"));
        var compiled = await CompileAsync(folder);
        Assert.NotNull(compiled.Release);
        Assert.NotNull(compiled.Model);

        var result = await ActivateAsync(compiled);

        Assert.Empty(result.Diagnostics);
        var table = Assert.Single(await ReadTablesAsync(_orderId));
        Assert.Equal(["id", "version", "f_number", "f_remark", "f_state"], table.Columns.Select(column => column.Name));
        Assert.True(compiled.Model.TryGetEntity("Order", out var entity));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var read = await RecordQueries.GetAsync(connection, compiled.Model, entity, first, CancellationToken);
        Assert.NotNull(read);
        Assert.Equal("first note", read.Values["remark"]?.GetValue<string>());
        Assert.False(read.Values.ContainsKey("note"));
        var empty = await RecordQueries.GetAsync(connection, compiled.Model, entity, second, CancellationToken);
        Assert.Null(empty?.Values["remark"]);
        Assert.Equal(["done", "draft"], (await ReadEnumRecordsAsync(_orderId)).Where(value => value.Field == "state").Select(value => value.Value).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(await ReadEnumRecordsAsync(_orderId), value => value.Field == "status");
        Assert.Equal(2, (await ReadAppliedMigrationsAsync()).Count);
        Assert.Equal(compiled.Release.Id, (await ReadActiveReleaseIdAsync()));
    }

    [Fact]
    public async Task A_pending_drop_field_drops_the_column_and_a_pending_drop_entity_drops_the_table_its_child_table_and_their_records()
    {
        using var folder = Application()
            .With("entities/order.json", Order(NumberField, NoteField))
            .With("entities/invoice.json", Invoice())
            .With("entities/line.json", Line());
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        Assert.Equal(2, await CountProvisionedEntitiesAsync(_invoiceId, _lineId));
        folder
            .With("entities/order.json", Order(NumberField))
            .Without("entities/invoice.json")
            .Without("entities/line.json")
            .With("migrations/0001-drop-note.json", Migration(_migrationId, "Order", """[{ "type": "dropField", "field": "note" }]"""))
            .With("migrations/0002-drop-invoice.json", Migration(Guid.NewGuid(), "Invoice", """[{ "type": "dropEntity" }]"""));
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled);

        Assert.Empty(result.Diagnostics);
        var order = Assert.Single(await ReadTablesAsync(_orderId));
        Assert.Equal(["id", "version", "f_number"], order.Columns.Select(column => column.Name));
        Assert.Empty(await ReadTablesAsync(_invoiceId));
        Assert.Empty(await ReadTablesAsync(_lineId));
        Assert.Equal(0, await CountProvisionedEntitiesAsync(_invoiceId, _lineId));
        Assert.Equal(2, (await ReadAppliedMigrationsAsync()).Count);
        Assert.Equal(compiled.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task Activating_the_same_release_again_and_then_a_later_release_does_not_apply_an_applied_migration_twice()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        await InsertOrderAsync("PO-1", "kept");
        folder
            .With("entities/order.json", Order(NumberField, RemarkField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"));
        var second = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(second)).Diagnostics);
        var applied = Assert.Single(await ReadAppliedMigrationsAsync());
        Assert.Equal(second.Release?.Id, applied.ReleaseId);
        var columns = Assert.Single(await ReadTablesAsync(_orderId)).Columns;

        Assert.Empty((await ActivateAsync(second)).Diagnostics);

        Assert.Equal(applied, Assert.Single(await ReadAppliedMigrationsAsync()));
        Assert.Equal(columns, Assert.Single(await ReadTablesAsync(_orderId)).Columns);

        folder.With("entities/order.json", Order(NumberField, RemarkField, PriorityField));
        var third = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(third)).Diagnostics);

        Assert.Equal(applied, Assert.Single(await ReadAppliedMigrationsAsync()));
        Assert.Equal([.. columns.Select(column => column.Name), "f_priority"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
        Assert.Equal(third.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task A_failing_second_migration_rolls_back_the_first_the_records_and_the_active_release()
    {
        using var folder = Application()
            .With("entities/order.json", Order(NumberField, NoteField))
            .With("entities/customer.json", Customer());
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        var customerId = Guid.NewGuid();
        await InsertAsync(_customerId, ("id", customerId), ("f_name", "Acme"));
        await InsertOrderAsync("PO-1", "kept");
        var blocker = $"blocker_{Guid.NewGuid():N}";
        await ExecuteAsync($"""CREATE TABLE public."{blocker}" (id uuid PRIMARY KEY, customer_id uuid REFERENCES {EntityNaming.QualifiedTable(EntityNaming.Table(_customerId))} (id))""");
        try
        {
            var before = await ReadStateAsync(_orderId, _customerId);
            folder
                .With("entities/order.json", Order(NumberField, RemarkField, PriorityField))
                .Without("entities/customer.json")
                .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"))
                .With("migrations/0002-drop-customer.json", Migration(Guid.NewGuid(), "Customer", """[{ "type": "dropEntity" }]"""));
            var second = await CompileAsync(folder);

            var exception = await Assert.ThrowsAsync<PostgresException>(() => ActivateAsync(second));

            Assert.Equal(PostgresErrorCodes.DependentObjectsStillExist, exception.SqlState);
            Assert.Equal(before, await ReadStateAsync(_orderId, _customerId));
            Assert.Equal(first.Release?.Id, await ReadActiveReleaseIdAsync());
            Assert.Empty(await ReadAppliedMigrationsAsync());
        }
        finally
        {
            await ExecuteAsync($"""DROP TABLE public."{blocker}" """.TrimEnd());
        }
    }

    [Fact]
    public async Task A_pending_rename_whose_site_path_another_application_holds_gives_a_diagnostic_and_changes_nothing()
    {
        var path = $"site-{Guid.NewGuid():N}";
        await ActivateOtherApplicationAtAsync(path);
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        await InsertOrderAsync("PO-1", "kept");
        var before = await ReadStateAsync(_orderId);
        WithSite(folder, path)
            .With("entities/order.json", Order(NumberField, RemarkField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"));
        var second = await CompileAsync(folder);

        // The store does not see the other application, so the pre-check passes and the write is refused.
        var result = await ActivateAsync(second, store => new ConflictHidingStore(store));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SitePathActiveForOtherApplication, "sites/main.json", "/path"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(before, await ReadStateAsync(_orderId));
        Assert.Equal(["id", "version", "f_number", "f_note"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
        Assert.Empty(await ReadAppliedMigrationsAsync());
        Assert.Equal(first.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task Changing_an_applied_migration_file_gives_a_diagnostic_at_the_file_and_provisions_nothing()
    {
        using var folder = await AppliedRenameAsync();
        var applied = Assert.Single(await ReadAppliedMigrationsAsync());
        folder
            .With("entities/order.json", Order(NumberField, RemarkField, PriorityField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark", name: "RenamedAgain"));
        var changed = await CompileAsync(folder);

        var result = await ActivateAsync(changed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ChangedMigrationHistory, "migrations/0001-rename-note.json", "", _migrationId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Equal(["id", "version", "f_number", "f_remark"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
        Assert.Equal(applied, Assert.Single(await ReadAppliedMigrationsAsync()));
        Assert.NotEqual(changed.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task Deleting_an_applied_migration_file_gives_a_diagnostic_at_the_manifest_and_provisions_nothing()
    {
        using var folder = await AppliedRenameAsync();
        var applied = Assert.Single(await ReadAppliedMigrationsAsync());
        folder
            .With("entities/order.json", Order(NumberField, RemarkField, PriorityField))
            .Without("migrations/0001-rename-note.json");
        var deleted = await CompileAsync(folder);

        var result = await ActivateAsync(deleted);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ChangedMigrationHistory, "application.json", "", _migrationId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Equal(["id", "version", "f_number", "f_remark"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
        Assert.Equal(applied, Assert.Single(await ReadAppliedMigrationsAsync()));
        Assert.NotEqual(deleted.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task A_pending_fill_gives_a_diagnostic_and_applies_nothing()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        var before = await ReadStateAsync(_orderId);
        folder.With("migrations/0001-fill-note.json", Migration(_migrationId, "Order", """[{ "type": "fill", "field": "note", "value": "'n/a'" }]"""));

        var result = await ActivateAsync(await CompileAsync(folder));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MigrationChangeNotApplied, "migrations/0001-fill-note.json", "/changes/0/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(before, await ReadStateAsync(_orderId));
        Assert.Empty(await ReadAppliedMigrationsAsync());
        Assert.Equal(first.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    [Fact]
    public async Task An_activation_without_a_pending_migration_cannot_overwrite_a_release_that_another_activation_commits_while_it_waits()
    {
        using var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        await InsertOrderAsync("PO-1", "first note");
        folder
            .With("entities/order.json", Order(NumberField, RemarkField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"));
        var second = await CompileAsync(folder);

        // The older release activates again and stops at the active release write. Its history check
        // is behind it, and its transaction still holds the schema lock.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var older = ActivateAsync(first, store => new PausingStore(store, reached, release));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        // The newer release must wait for the lock. Without it, it commits the rename now and the older
        // release overwrites it with tables it does not match.
        var newer = ActivateAsync(second);
        await WaitUntilAsync(async () => newer.IsCompleted || await CountWaitingAdvisoryLocksAsync() > 0);
        release.SetResult();

        Assert.Empty((await older).Diagnostics);
        Assert.Empty((await newer).Diagnostics);
        Assert.Equal(second.Release?.Id, await ReadActiveReleaseIdAsync());
        Assert.Equal(["id", "version", "f_number", "f_remark"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
        Assert.Equal(_migrationId, Assert.Single(await ReadAppliedMigrationsAsync()).MigrationId);
    }

    [Fact]
    public async Task A_waiting_drop_entity_finds_the_entity_under_the_name_a_release_committed_while_it_waited()
    {
        using var folder = Application()
            .With("entities/order.json", Order(NumberField))
            .With("entities/customer.json", Customer());
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        Assert.Equal(1, await CountProvisionedEntitiesAsync(_customerId));

        // The second release renames the entity Customer to Client. The third drops it by its new name.
        folder.With("entities/customer.json", EntityFile("Client", _customerId, """{ "name": "name", "type": "text" }"""));
        var second = await CompileAsync(folder);
        folder
            .Without("entities/customer.json")
            .With("migrations/0001-drop-client.json", Migration(_migrationId, "Client", """[{ "type": "dropEntity" }]"""));
        var third = await CompileAsync(folder);

        // The second release stops at the active release write and still holds the schema lock.
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renaming = ActivateAsync(second, store => new PausingStore(store, reached, release));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);

        // The third release waits for the lock. It must read the stored entity ids after the wait, or it
        // looks for Client in the first release, finds nothing, skips the table and gives AXC0017.
        var dropping = ActivateAsync(third);
        await WaitUntilAsync(async () => dropping.IsCompleted || await CountWaitingAdvisoryLocksAsync() > 0);
        release.SetResult();

        Assert.Empty((await renaming).Diagnostics);
        Assert.Empty((await dropping).Diagnostics);
        Assert.Empty(await ReadTablesAsync(_customerId));
        Assert.Equal(0, await CountProvisionedEntitiesAsync(_customerId));
        Assert.Equal(_migrationId, Assert.Single(await ReadAppliedMigrationsAsync()).MigrationId);
        Assert.Equal(third.Release?.Id, await ReadActiveReleaseIdAsync());
    }

    // R1 has a note field and R2 renames it to remark, so one migration is applied.
    private async Task<TemporaryFolder> AppliedRenameAsync()
    {
        var folder = Application().With("entities/order.json", Order(NumberField, NoteField));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        folder
            .With("entities/order.json", Order(NumberField, RemarkField))
            .With("migrations/0001-rename-note.json", RenameMigration(_migrationId, "note", "remark"));
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        return folder;
    }

    private async Task ActivateOtherApplicationAtAsync(string path)
    {
        var otherName = $"App{Guid.NewGuid():N}";
        using var other = WithSite(
            new TemporaryFolder()
                .With("application.json", Manifest(Guid.NewGuid(), otherName))
                .With("entities/order.json", EntityFile("Order", Guid.NewGuid(), NumberField)),
            path);
        Assert.Empty((await ActivateAsync(await CompileAsync(other))).Diagnostics);
    }

    private TemporaryFolder Application() => new TemporaryFolder().With("application.json", Manifest(_applicationId, _name));

    private static string Manifest(Guid id, string name) =>
        $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""";

    private static string EntityFile(string name, Guid id, params string[] fields) =>
        $$"""
        { "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private string Order(params string[] fields) => EntityFile("Order", _orderId, fields);

    private string Customer() => EntityFile("Customer", _customerId, """{ "name": "name", "type": "text" }""");

    private string Invoice() =>
        EntityFile("Invoice", _invoiceId, NumberField, """{ "name": "lines", "type": "child-collection", "target": "Line" }""");

    private string Line() => EntityFile("Line", _lineId, """{ "name": "description", "type": "text" }""");

    // The name defaults to one derived from the id, because names are unique in an application.
    private static string Migration(Guid id, string entity, string changes, string? name = null) =>
        $$"""
        { "id": "{{id}}", "kind": "migration", "name": "{{name ?? $"M{id:N}"}}", "formatVersion": 1, "entity": "{{entity}}", "changes": {{changes}} }
        """;

    private static string RenameMigration(Guid id, string from, string to, string? name = null) =>
        Migration(id, "Order", $$"""[{ "type": "renameField", "from": "{{from}}", "to": "{{to}}" }]""", name);

    /// <summary>Gives the folder a site at <paramref name="path"/> whose navigation opens a table page of orders.</summary>
    private static TemporaryFolder WithSite(TemporaryFolder folder, string path) =>
        folder
            .With("texts/en.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders" } }
                """)
            .With("pages/orders.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "page", "name": "Orders", "formatVersion": 1,
                  "title": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "entity": "Order" } ] }
                """)
            .With("sites/main.json", $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "site", "name": "Main", "formatVersion": 1, "path": "{{path}}",
                  "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
                  "navigation": [ { "page": "Orders", "label": { "textKey": "nav.orders" } } ] }
                """);

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    // One connection for the store and the data context: the active release is written in the
    // provisioning transaction when a migration is pending.
    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, Func<IActiveReleaseStore, IActiveReleaseStore>? wrap = null)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        IActiveReleaseStore store = new ActiveReleaseStore(configuration);
        return await ReleaseActivator.ActivateAsync(compiled, wrap?.Invoke(store) ?? store, data, ActivationBindings.None("default"), CancellationToken);
    }

    private async Task<Guid> InsertOrderAsync(string number, string? note)
    {
        var id = Guid.NewGuid();
        await InsertAsync(_orderId, ("id", id), ("f_number", number), ("f_note", note));
        return id;
    }

    private async Task InsertAsync(Guid entityId, params (string Column, object? Value)[] values)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        var columns = string.Join(", ", values.Select(value => EntityNaming.Quote(value.Column)));
        var parameters = string.Join(", ", values.Select((_, index) => $"@p{index}"));
        await using var command = dataSource.CreateCommand($"INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))} ({columns}) VALUES ({parameters})");
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"p{index}", values[index].Value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    /// <summary>The catalog table of the entity, if it exists.</summary>
    private async Task<List<CatalogTable>> ReadTablesAsync(Guid entityId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var catalog = await CatalogReader.ReadAsync(connection, null, CancellationToken);
        return [.. catalog.Tables.Where(table => table.Name == EntityNaming.Table(entityId))];
    }

    private async Task<int> CountProvisionedEntitiesAsync(params Guid[] entityIds)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.provisioned_entities WHERE entity_id = ANY(@ids)");
        command.Parameters.AddWithValue("ids", entityIds);
        return (int)Assert.IsType<long>(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task<List<(string Field, string Value)>> ReadEnumRecordsAsync(Guid entityId)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT field_name, value FROM axis.provisioned_enum_values WHERE entity_id = @id");
        command.Parameters.AddWithValue("id", entityId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var values = new List<(string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            values.Add((reader.GetString(0), reader.GetString(1)));
        }

        return values;
    }

    private async Task<List<AppliedMigration>> ReadAppliedMigrationsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            "SELECT migration_id, release_id, content_hash, applied_at FROM axis.applied_migrations WHERE application_id = @id ORDER BY migration_id");
        command.Parameters.AddWithValue("id", _applicationId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<AppliedMigration>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new AppliedMigration(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
            await Task.Delay(50, CancellationToken);
        }
    }

    private async Task<long> CountWaitingAdvisoryLocksAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted");
        return Assert.IsType<long>(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task<Guid?> ReadActiveReleaseIdAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @id");
        command.Parameters.AddWithValue("id", _applicationId);
        return await command.ExecuteScalarAsync(CancellationToken) as Guid?;
    }

    /// <summary>
    /// Everything a failed activation must leave alone: the columns and rows of the entity tables,
    /// the provisioning records, the applied migrations and the active release.
    /// </summary>
    private async Task<List<string>> ReadStateAsync(params Guid[] entityIds)
    {
        var state = new List<string>();
        foreach (var entityId in entityIds)
        {
            foreach (var table in await ReadTablesAsync(entityId))
            {
                state.Add($"{table.Name}: {string.Join(", ", table.Columns.Select(column => $"{column.Name} {column.Type}"))}");
                state.AddRange(await ReadScalarsAsync($"SELECT to_jsonb(t)::text FROM {EntityNaming.QualifiedTable(table.Name)} t ORDER BY t.id"));
            }

            state.AddRange(await ReadScalarsAsync($"SELECT to_jsonb(e)::text FROM axis.provisioned_entities e WHERE entity_id = '{entityId}'"));
            state.AddRange(await ReadScalarsAsync($"SELECT to_jsonb(v)::text FROM axis.provisioned_enum_values v WHERE entity_id = '{entityId}' ORDER BY field_name, value"));
        }

        state.AddRange(await ReadScalarsAsync($"SELECT to_jsonb(m)::text FROM axis.applied_migrations m WHERE application_id = '{_applicationId}' ORDER BY migration_id"));
        state.AddRange(await ReadScalarsAsync($"SELECT to_jsonb(a)::text FROM axis.active_releases a WHERE application_id = '{_applicationId}'"));
        return state;
    }

    private async Task<List<string>> ReadScalarsAsync(string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private sealed record AppliedMigration(Guid MigrationId, Guid ReleaseId, string ContentHash, DateTimeOffset AppliedAt);
}
