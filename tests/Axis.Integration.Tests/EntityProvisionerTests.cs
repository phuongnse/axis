using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Axis.Data.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Axis.Data.Tests.Models;

namespace Axis.Integration.Tests;

public sealed class EntityProvisionerTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids, so tests sharing the database do not see
    // each other's tables and records.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _lineId = Guid.NewGuid();

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrations_history_is_kept_in_the_module_table()
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("""SELECT "MigrationId" FROM axis.__data_migrations""");

        var migration = await command.ExecuteScalarAsync(CancellationToken);

        Assert.EndsWith("_CreateProvisionedEntities", Assert.IsType<string>(migration), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_database_gets_one_table_per_entity_matching_the_model_and_its_records()
    {
        var model = BaseModel();

        var result = await ProvisionAsync(model);

        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.Statements);
        await AssertCatalogMatchesAsync(model, hasRows: false);

        var records = await ReadRecordsAsync(model);
        Assert.Equal(
            model.Entities.Select(entity => new ProvisionedEntity(entity.Id, _applicationId, EntityNaming.Table(entity.Id))).OrderBy(entity => entity.EntityId),
            records.Entities);
        Assert.Equal(
            [
                new ProvisionedEnumValue(_orderId, "status", "approved"),
                new ProvisionedEnumValue(_orderId, "status", "draft"),
                new ProvisionedEnumValue(_orderId, "status", "submitted"),
            ],
            records.EnumValues);
    }

    [Fact]
    public async Task Added_optional_field_adds_the_column_and_keeps_existing_rows()
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var rowId = await InsertCustomerAsync();
        var changed = ChangeEntity(model, _customerId, fields => [.. fields, Field("phone", FieldType.Text, maxLength: 30)]);

        var result = await ProvisionAsync(changed);

        Assert.Empty(result.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{EntityNaming.Table(_customerId)}" ADD COLUMN "f_phone" character varying(30)"""], result.Statements);
        await AssertCatalogMatchesAsync(changed, hasRows: false, tablesWithRows: _customerId);
        Assert.Equal([rowId], await ReadIdsAsync(_customerId));
    }

    [Fact]
    public async Task Child_table_has_owner_and_position_and_its_rows_are_deleted_with_their_owner()
    {
        var model = ChildModel(BaseModel());

        var result = await ProvisionAsync(model);

        Assert.Empty(result.Diagnostics);
        await AssertCatalogMatchesAsync(model, hasRows: false);
        var ownerId = await InsertCustomerAsync();
        var otherOwnerId = await InsertCustomerAsync();
        await InsertLineAsync(ownerId, 0);
        await InsertLineAsync(ownerId, 1);
        var otherLineId = await InsertLineAsync(otherOwnerId, 0);

        await ExecuteAsync($"""DELETE FROM {EntityNaming.QualifiedTable(EntityNaming.Table(_customerId))} WHERE "id" = @id""", ownerId);

        Assert.Equal([otherLineId], await ReadIdsAsync(_lineId));
    }

    [Fact]
    public async Task Added_child_collection_creates_only_the_child_table_and_keeps_the_owner_rows()
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var rowId = await InsertCustomerAsync();
        var changed = ChildModel(model);

        var result = await ProvisionAsync(changed);

        Assert.Empty(result.Diagnostics);
        var lineTable = EntityNaming.Table(_lineId);
        Assert.Equal(
            [
                $"""CREATE TABLE "entities"."{lineTable}" ("id" uuid NOT NULL, "owner_id" uuid NOT NULL, "position" integer NOT NULL, "f_description" character varying(200), CONSTRAINT "pk_{lineTable}" PRIMARY KEY ("id"))""",
                $"""ALTER TABLE "entities"."{lineTable}" ADD CONSTRAINT "{EntityNaming.ForeignKey(lineTable, "owner_id")}" FOREIGN KEY ("owner_id") REFERENCES "entities"."{EntityNaming.Table(_customerId)}" ("id") ON DELETE CASCADE""",
            ],
            result.Statements);
        await AssertCatalogMatchesAsync(changed, hasRows: false, tablesWithRows: _customerId);
        Assert.Equal([rowId], await ReadIdsAsync(_customerId));
    }

    [Fact]
    public async Task Missing_version_column_is_added_back_and_fills_existing_rows()
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var customerTable = EntityNaming.QualifiedTable(EntityNaming.Table(_customerId));
        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            await using var command = dataSource.CreateCommand($"""ALTER TABLE {customerTable} DROP COLUMN "version" """);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var rowId = await InsertCustomerAsync();

        var result = await ProvisionAsync(model);

        Assert.Empty(result.Diagnostics);
        Assert.Equal([$"""ALTER TABLE {customerTable} ADD COLUMN "version" bigint NOT NULL DEFAULT 1"""], result.Statements);
        var table = Assert.Single(await ReadTablesAsync(model), table => table.Name == EntityNaming.Table(_customerId));
        Assert.True(table.HasRows);
        Assert.Equal(
            new CatalogColumn("version", "bigint", NotNull: true, Unique: false, ReferencedTable: null),
            Assert.Single(table.Columns, column => column.Name == "version"));
        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            await using var command = dataSource.CreateCommand($"""SELECT "version" FROM {customerTable} WHERE "id" = @id""");
            command.Parameters.AddWithValue("id", rowId);
            Assert.Equal(1L, await command.ExecuteScalarAsync(CancellationToken));
        }
    }

    [Fact]
    public async Task Label_only_change_applies_nothing()
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var changed = model with
        {
            Entities =
            [
                .. model.Entities.Select(entity => entity with
                {
                    Label = new TextReference($"{entity.Name}.renamed"),
                    Fields = [.. entity.Fields.Select(field => field with { Label = new TextReference($"{entity.Name}.{field.Name}") })],
                }),
            ],
        };

        var result = await ProvisionAsync(changed);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Statements);
        await AssertCatalogMatchesAsync(model, hasRows: false);
    }

    [Fact]
    public async Task Added_enum_value_adds_one_record_without_statements()
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var before = await ReadRecordsAsync(model);
        var changed = ChangeEntity(model, _orderId, fields => [.. fields.Select(field => field.Name == "status" ? field with { Values = [.. field.Values!, "rejected"] } : field)]);

        var result = await ProvisionAsync(changed);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.Statements);
        var after = await ReadRecordsAsync(model);
        Assert.Equal(before.Entities, after.Entities);
        Assert.Equal(
            [new ProvisionedEnumValue(_orderId, "status", "rejected")],
            after.EnumValues.Except(before.EnumValues));
        Assert.Equal(before.EnumValues.Count + 1, after.EnumValues.Count);
    }

    [Theory]
    [InlineData("removed field", DiagnosticCodes.RemovedField, "entities/order.json", "/fields")]
    [InlineData("changed type", DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", "/fields/6/type")]
    [InlineData("changed target", DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", "/fields/2/target")]
    [InlineData("removed enum value", DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", "/fields/11/values")]
    [InlineData("removed entity", DiagnosticCodes.RemovedEntity, "application.json", "")]
    [InlineData("entity owned by another application", DiagnosticCodes.EntityOwnedByOtherApplication, "entities/other.json", "/id")]
    public async Task Rejected_change_returns_its_diagnostic_and_changes_nothing(string change, string code, string file, string path)
    {
        var model = BaseModel();
        await ProvisionAsync(model);
        var other = Entity(Guid.NewGuid(), "Other", "entities/other.json", Field("name", FieldType.Text));
        await ProvisionAsync(WithApplicationId(Application(other), Guid.NewGuid()));

        var changed = change switch
        {
            "removed field" => ChangeEntity(model, _orderId, fields => [.. fields.Where(field => field.Name != "notes")]),
            "changed type" => ChangeOrderField(model, "quantity", quantity => quantity with { Type = FieldType.Decimal }),
            "changed target" => ChangeOrderField(model, "customer", customer => customer with { Target = new EntityReference(_supplierId, "Supplier") }),
            "removed enum value" => ChangeOrderField(model, "status", status => status with { Values = ["draft", "approved"] }),
            "removed entity" => model with { Entities = [.. model.Entities.Where(entity => entity.Id != _supplierId)] },
            "entity owned by another application" => model with { Entities = [.. model.Entities, other] },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        // Every rejected model also adds an optional field, so an unchanged catalog shows no DDL ran.
        changed = ChangeEntity(changed, _orderId, fields => [.. fields, Field("extra", FieldType.Integer)]);
        var tablesBefore = await ReadTablesAsync(model, other);
        var recordsBefore = await ReadRecordsAsync(model, other);

        var result = await ProvisionAsync(changed);

        var diagnostic = Assert.Single(result.Diagnostics);
        var resourceId = change switch
        {
            "removed entity" => _supplierId,
            "entity owned by another application" => other.Id,
            _ => _orderId,
        };
        Assert.Equal((code, file, path, resourceId), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Empty(result.Statements);
        AssertSameTables(tablesBefore, await ReadTablesAsync(model, other));
        var recordsAfter = await ReadRecordsAsync(model, other);
        Assert.Equal(recordsBefore.Entities, recordsAfter.Entities);
        Assert.Equal(recordsBefore.EnumValues, recordsAfter.EnumValues);
    }

    [Fact]
    public async Task Failing_statement_rolls_back_every_table_and_record_of_the_call()
    {
        var model = BaseModel();

        // A relation the catalog does not read takes the order table's name, so the plan still
        // creates the table and that second CREATE TABLE fails while applying.
        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            await using var command = dataSource.CreateCommand(
                $"""CREATE SCHEMA IF NOT EXISTS "entities"; CREATE SEQUENCE {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))}""");
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(() => ProvisionAsync(model));

        Assert.Equal(PostgresErrorCodes.DuplicateTable, exception.SqlState);
        Assert.Empty(await ReadTablesAsync(model));
        await using var context = database.CreateContext();
        Assert.False(await context.ProvisionedEntities.AnyAsync(entity => entity.ApplicationId == _applicationId, CancellationToken));
        var entityIds = model.Entities.Select(entity => entity.Id).ToList();
        Assert.False(await context.ProvisionedEnumValues.AnyAsync(value => entityIds.Contains(value.EntityId), CancellationToken));
    }

    [Fact]
    public async Task Concurrent_provisions_of_the_same_model_leave_one_table_per_entity()
    {
        var model = BaseModel();
        var contexts = Enumerable.Range(0, 8).Select(_ => database.CreateContext()).ToList();
        try
        {
            var results = await Task.WhenAll(contexts.Select(context => EntityProvisioner.ProvisionAsync(model, context, CancellationToken)));

            Assert.All(results, result => Assert.Empty(result.Diagnostics));
            Assert.Single(results, result => result.Statements.Count > 0);
            await AssertCatalogMatchesAsync(model, hasRows: false);
            Assert.Equal(model.Entities.Count, (await ReadRecordsAsync(model)).Entities.Count);
        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Release_compiled_from_a_folder_provisions_its_tables_and_follows_an_added_field()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "Sample", "formatVersion": 1 }""")
            .With("entities/order.json", OrderFile(_orderId, NumberField));

        var first = await CompileAsync(folder);
        Assert.NotNull(first.Release);
        Assert.NotNull(first.Model);
        var provisioned = await ProvisionAsync(first.Model);
        Assert.Empty(provisioned.Diagnostics);
        await using (var dataSource = NpgsqlDataSource.Create(database.ConnectionString))
        {
            await using var command = dataSource.CreateCommand($"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))} ("id", "f_number") VALUES (@id, 'PO-1')""");
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        folder.With("entities/order.json", OrderFile(_orderId, NumberField, """{ "name": "note", "type": "text" }"""));
        var second = await CompileAsync(folder);
        Assert.NotNull(second.Release);
        Assert.NotEqual(first.Release.Id, second.Release.Id);
        Assert.NotNull(second.Model);
        var extended = await ProvisionAsync(second.Model);

        Assert.Empty(extended.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{EntityNaming.Table(_orderId)}" ADD COLUMN "f_note" text"""], extended.Statements);
        var table = Assert.Single(await ReadTablesAsync(second.Model));
        Assert.True(table.HasRows);
        Assert.Equal(
            [
                new CatalogColumn("id", "uuid", NotNull: true, Unique: false, ReferencedTable: null),
                new CatalogColumn("version", "bigint", NotNull: true, Unique: false, ReferencedTable: null),
                new CatalogColumn("f_number", "character varying(20)", NotNull: true, Unique: false, ReferencedTable: null),
                new CatalogColumn("f_note", "text", NotNull: false, Unique: false, ReferencedTable: null),
            ],
            table.Columns);
        Assert.Single(await ReadIdsAsync(_orderId));

        // Compiling the unchanged folder returns the stored release, still with its model.
        var again = await CompileAsync(folder);
        Assert.Equal(second.Release.Id, again.Release?.Id);
        Assert.NotNull(again.Model);
        var unchanged = await ProvisionAsync(again.Model);
        Assert.Empty(unchanged.Diagnostics);
        Assert.Empty(unchanged.Statements);
    }

    private static string OrderFile(Guid id, params string[] fields) =>
        $$"""
        { "id": "{{id}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    // Customer and Order reference each other, Order references itself and Order has every field type.
    private ApplicationModel BaseModel()
    {
        var customerShell = Entity(_customerId, "Customer", "entities/customer.json");
        var orderShell = Entity(_orderId, "Order", "entities/order.json");
        var customer = customerShell with
        {
            Fields =
            [
                Field("name", FieldType.Text, required: true, maxLength: 200),
                Field("email", FieldType.Text, unique: true, maxLength: 100),
                Field("lastOrder", FieldType.Reference, target: orderShell),
            ],
        };
        var order = orderShell with
        {
            Fields =
            [
                Field("code", FieldType.Text, required: true, unique: true, maxLength: 20),
                Field("title", FieldType.Text, required: true, maxLength: 100),
                Field("customer", FieldType.Reference, required: true, target: customerShell),
                Field("parent", FieldType.Reference, target: orderShell),
                Field("total", FieldType.Decimal, precision: 18, scale: 2),
                Field("weight", FieldType.Decimal),
                Field("quantity", FieldType.Integer),
                Field("paid", FieldType.Boolean),
                Field("due", FieldType.Date),
                Field("placedAt", FieldType.DateTime),
                Field("notes", FieldType.Text),
                Field("status", FieldType.Enum, required: true, values: ["draft", "submitted", "approved"]),
            ],
        };
        var supplier = Entity(_supplierId, "Supplier", "entities/supplier.json", Field("name", FieldType.Text));
        return WithApplicationId(Application(customer, order, supplier), _applicationId);
    }

    // Customer owns Line through a child collection between its other fields.
    private ApplicationModel ChildModel(ApplicationModel model)
    {
        var line = Entity(_lineId, "Line", "entities/line.json", Field("description", FieldType.Text, maxLength: 200));
        var changed = ChangeEntity(model, _customerId, fields => [fields[0], Field("lines", FieldType.ChildCollection, target: line), .. fields.Skip(1)]);
        return changed with { Entities = [.. changed.Entities, line] };
    }

    private static ApplicationModel WithApplicationId(ApplicationModel model, Guid applicationId) =>
        model with { Manifest = model.Manifest with { Id = applicationId } };

    private static ApplicationModel ChangeEntity(ApplicationModel model, Guid entityId, Func<IReadOnlyList<FieldModel>, IReadOnlyList<FieldModel>> change) =>
        model with { Entities = [.. model.Entities.Select(entity => entity.Id == entityId ? entity with { Fields = change(entity.Fields) } : entity)] };

    private ApplicationModel ChangeOrderField(ApplicationModel model, string name, Func<FieldModel, FieldModel> change) =>
        ChangeEntity(model, _orderId, fields => [.. fields.Select(field => field.Name == name ? change(field) : field)]);

    private async Task<ProvisioningResult> ProvisionAsync(ApplicationModel model)
    {
        await using var context = database.CreateContext();
        return await EntityProvisioner.ProvisionAsync(model, context, CancellationToken);
    }

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        return await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
    }

    private async Task<Guid> InsertCustomerAsync()
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_customerId))} ("id", "f_name") VALUES (@id, 'Ada')""");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task<Guid> InsertLineAsync(Guid ownerId, int position)
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(
            $"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_lineId))} ("id", "owner_id", "position", "f_description") VALUES (@id, @owner, @position, 'Desk')""");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("owner", ownerId);
        command.Parameters.AddWithValue("position", position);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    private async Task<List<Guid>> ReadIdsAsync(Guid entityId)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""SELECT "id" FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))}""");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    /// <summary>The catalog tables of the given models' entities, in name order.</summary>
    private async Task<List<CatalogTable>> ReadTablesAsync(params ApplicationModel[] models)
    {
        var names = models.SelectMany(model => model.Entities).Select(entity => EntityNaming.Table(entity.Id)).ToHashSet(StringComparer.Ordinal);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var catalog = await CatalogReader.ReadAsync(connection, null, CancellationToken);
        return [.. catalog.Tables.Where(table => names.Contains(table.Name))];
    }

    private Task<List<CatalogTable>> ReadTablesAsync(ApplicationModel model, EntityModel other) =>
        ReadTablesAsync(model, Application(other));

    /// <summary>The records of this test's application and of the given models' entities, in a stable order.</summary>
    private async Task<ProvisioningRecords> ReadRecordsAsync(params ApplicationModel[] models)
    {
        var entityIds = models.SelectMany(model => model.Entities).Select(entity => entity.Id).ToList();
        await using var context = database.CreateContext();
        var entities = await context.ProvisionedEntities
            .AsNoTracking()
            .Where(entity => entity.ApplicationId == _applicationId || entityIds.Contains(entity.EntityId))
            .Select(entity => new ProvisionedEntity(entity.EntityId, entity.ApplicationId, entity.TableName))
            .ToListAsync(CancellationToken);
        var enumValues = await context.ProvisionedEnumValues
            .AsNoTracking()
            .Where(value => entityIds.Contains(value.EntityId))
            .Select(value => new ProvisionedEnumValue(value.EntityId, value.FieldName, value.Value))
            .ToListAsync(CancellationToken);
        return new ProvisioningRecords(
            [.. entities.OrderBy(entity => entity.EntityId)],
            [.. enumValues.OrderBy(value => value.EntityId).ThenBy(value => value.FieldName, StringComparer.Ordinal).ThenBy(value => value.Value, StringComparer.Ordinal)]);
    }

    private Task<ProvisioningRecords> ReadRecordsAsync(ApplicationModel model, EntityModel other) =>
        ReadRecordsAsync(model, Application(other));

    /// <summary>
    /// Asserts that the model's tables exist with exactly the columns, types, NOT NULL, unique
    /// constraints and foreign key targets the model expects.
    /// </summary>
    private async Task AssertCatalogMatchesAsync(ApplicationModel model, bool hasRows, Guid? tablesWithRows = null)
    {
        var expected = Catalog(model, hasRows).Tables
            .Select(table => tablesWithRows is { } id && table.Name == EntityNaming.Table(id) ? table with { HasRows = true } : table)
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToList();
        AssertSameTables(expected, await ReadTablesAsync(model));
    }

    private static void AssertSameTables(IReadOnlyList<CatalogTable> expected, IReadOnlyList<CatalogTable> actual)
    {
        Assert.Equal(expected.Select(table => (table.Name, table.HasRows)), actual.Select(table => (table.Name, table.HasRows)));
        foreach (var (expectedTable, actualTable) in expected.Zip(actual))
        {
            Assert.Equal(expectedTable.Columns, actualTable.Columns);
        }
    }
}
