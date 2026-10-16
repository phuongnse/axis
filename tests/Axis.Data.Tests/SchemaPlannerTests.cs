using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class SchemaPlannerTests
{
    private const string CustomerTable = "e_22222222222242228222222222222222";
    private const string OrderTable = "e_11111111111141118111111111111111";
    private const string LineTable = "e_44444444444444448444444444444444";

    private static readonly Guid _customerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid _orderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _supplierId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid _lineId = Guid.Parse("44444444-4444-4444-8444-444444444444");

    // The entity and field of each destructive change, in order.
    private static List<(Guid EntityId, string? Field)> DestructiveTargets(SchemaPlan plan) =>
        [.. plan.DestructiveChanges.Select(change => (change.EntityId, change.Field))];

    [Fact]
    public void Empty_catalog_creates_every_table_before_any_foreign_key_and_records_entities_and_enum_values()
    {
        // Customer and Order reference each other, and Order references itself.
        var customerShell = Entity(_customerId, "Customer", "customer.json");
        var orderShell = Entity(_orderId, "Order", "order.json");
        var customer = customerShell with
        {
            Fields =
            [
                Field("name", FieldType.Text, required: true, maxLength: 200),
                Field("lastOrder", FieldType.Reference, target: orderShell),
            ],
        };
        var order = orderShell with
        {
            Fields =
            [
                Field("code", FieldType.Text, required: true, unique: true, maxLength: 20),
                Field("customer", FieldType.Reference, required: true, target: customerShell),
                Field("parent", FieldType.Reference, target: orderShell),
                Field("total", FieldType.Decimal, precision: 18, scale: 2),
                Field("quantity", FieldType.Integer),
                Field("paid", FieldType.Boolean),
                Field("due", FieldType.Date),
                Field("placedAt", FieldType.DateTime),
                Field("notes", FieldType.Text),
                Field("status", FieldType.Enum, required: true, values: ["draft", "submitted", "approved"]),
            ],
        };

        var plan = SchemaPlanner.Plan(Application(customer, order), CatalogSnapshot.Empty, ProvisioningRecords.Empty);

        Assert.Empty(plan.Diagnostics);
        Assert.False(plan.HasErrors);
        Assert.Equal(
            [
                $"""CREATE TABLE "entities"."{CustomerTable}" ("id" uuid NOT NULL, "version" bigint NOT NULL DEFAULT 1, "f_name" character varying(200) NOT NULL, "f_lastorder" uuid, CONSTRAINT "pk_{CustomerTable}" PRIMARY KEY ("id"))""",
                $"""CREATE TABLE "entities"."{OrderTable}" ("id" uuid NOT NULL, "version" bigint NOT NULL DEFAULT 1, "f_code" character varying(20) NOT NULL, "f_customer" uuid NOT NULL, "f_parent" uuid, "f_total" numeric(18,2), "f_quantity" bigint, "f_paid" boolean, "f_due" date, "f_placedat" timestamp with time zone, "f_notes" text, "f_status" text NOT NULL, CONSTRAINT "pk_{OrderTable}" PRIMARY KEY ("id"), CONSTRAINT "{EntityNaming.Unique(OrderTable, "f_code")}" UNIQUE ("f_code"))""",
                ForeignKey(CustomerTable, "f_lastorder", OrderTable),
                ForeignKey(OrderTable, "f_customer", CustomerTable),
                ForeignKey(OrderTable, "f_parent", OrderTable),
            ],
            plan.Statements);
        Assert.Equal(
            [new ProvisionedEntity(_customerId, Models.ApplicationId, CustomerTable), new ProvisionedEntity(_orderId, Models.ApplicationId, OrderTable)],
            plan.NewEntities);
        Assert.Equal(
            [
                new ProvisionedEnumValue(_orderId, "status", "draft"),
                new ProvisionedEnumValue(_orderId, "status", "submitted"),
                new ProvisionedEnumValue(_orderId, "status", "approved"),
            ],
            plan.NewEnumValues);
    }

    [Fact]
    public void Added_optional_field_on_a_table_with_rows_adds_one_column()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields, Field("priority", FieldType.Integer)]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{OrderTable}" ADD COLUMN "f_priority" bigint"""], plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.NewEnumValues);
    }

    [Fact]
    public void Missing_version_column_on_a_table_with_rows_is_added_before_the_field_changes()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields, Field("priority", FieldType.Integer)]);
        var catalog = ChangeOrderColumns(Catalog(model, hasRows: true), columns => columns.Where(column => column.Name != EntityNaming.VersionColumn));

        var plan = SchemaPlanner.Plan(changed, catalog, Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(
            [
                $"""ALTER TABLE "entities"."{OrderTable}" ADD COLUMN "version" bigint NOT NULL DEFAULT 1""",
                $"""ALTER TABLE "entities"."{OrderTable}" ADD COLUMN "f_priority" bigint""",
            ],
            plan.Statements);
    }

    [Theory]
    [InlineData("bigint", true)]
    [InlineData("integer", true)]
    [InlineData("bigint", false)]
    public void Existing_version_column_is_never_compared_or_reported(string type, bool notNull)
    {
        var model = BaseModel();
        var catalog = ChangeOrderColumns(
            Catalog(model, hasRows: true),
            columns => columns.Select(column => column.Name == EntityNaming.VersionColumn ? column with { Type = type, NotNull = notNull } : column));

        var plan = SchemaPlanner.Plan(model, catalog, Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Added_required_unique_reference_on_a_table_without_rows_adds_the_column_and_its_constraints()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields, Field("billTo", FieldType.Reference, required: true, unique: true, target: Customer())]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: false), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(
            [
                $"""ALTER TABLE "entities"."{OrderTable}" ADD COLUMN "f_billto" uuid NOT NULL""",
                $"""ALTER TABLE "entities"."{OrderTable}" ADD CONSTRAINT "{EntityNaming.Unique(OrderTable, "f_billto")}" UNIQUE ("f_billto")""",
                ForeignKey(OrderTable, "f_billto", CustomerTable),
            ],
            plan.Statements);
    }

    [Fact]
    public void Label_only_change_and_entity_rename_plan_nothing()
    {
        var model = BaseModel();
        var customer = Customer() with { Name = "Client", Label = new TextReference("client.label") };
        var changed = Application(
            customer,
            Order(customer) with
            {
                Label = new TextReference("order.renamed"),
                Fields = [.. Order(customer).Fields.Select(field => field with { Label = new TextReference($"order.{field.Name}") })],
            },
            Supplier());

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Empty(plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.NewEnumValues);
    }

    [Theory]
    [InlineData("widen length", """ALTER COLUMN "f_title" TYPE character varying(200)""")]
    [InlineData("widen to text", """ALTER COLUMN "f_title" TYPE text""")]
    [InlineData("drop required", """ALTER COLUMN "f_title" DROP NOT NULL""")]
    public void Compatible_column_change_is_applied_with_one_statement(string change, string statement)
    {
        var model = BaseModel();
        var changed = ChangeOrderField(model, 0, change switch
        {
            "widen length" => title => title with { MaxLength = 200 },
            "widen to text" => title => title with { MaxLength = null },
            _ => title => title with { Required = false },
        });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{OrderTable}" {statement}"""], plan.Statements);
    }

    [Fact]
    public void Dropping_unique_drops_the_unique_constraint()
    {
        var model = BaseModel();
        var changed = ChangeOrderField(model, 1, code => code with { Unique = false });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"ALTER TABLE \"entities\".\"{OrderTable}\" DROP CONSTRAINT \"{EntityNaming.Unique(OrderTable, "f_code")}\""], plan.Statements);
    }

    [Fact]
    public void Reference_column_without_a_foreign_key_gets_one()
    {
        var model = BaseModel();
        var catalog = Catalog(model, hasRows: true);
        catalog = new CatalogSnapshot(
            [
                .. catalog.Tables.Select(table => table.Name != OrderTable
                    ? table
                    : table with { Columns = [.. table.Columns.Select(column => column.Name == "f_customer" ? column with { ReferencedTable = null } : column)] }),
            ]);

        var plan = SchemaPlanner.Plan(model, catalog, Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([ForeignKey(OrderTable, "f_customer", CustomerTable)], plan.Statements);
    }

    [Fact]
    public void Added_enum_value_is_recorded_without_statements()
    {
        var model = BaseModel();
        var changed = ChangeOrderField(model, 3, status => status with { Values = ["draft", "submitted", "Draft"] });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Empty(plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Equal([new ProvisionedEnumValue(_orderId, "status", "Draft")], plan.NewEnumValues);
    }

    [Fact]
    public void Removed_field_is_reported_at_the_fields_naming_the_column()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields.Where(field => field.Name != "amount")]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.RemovedField, "entities/order.json", "/fields", _orderId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'f_amount'", diagnostic.Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Theory]
    [InlineData("changed type", "/fields/4/type")]
    [InlineData("narrowed length", "/fields/0/maxLength")]
    [InlineData("text to limited length", "/fields/5/maxLength")]
    [InlineData("changed target", "/fields/2/target")]
    [InlineData("removed enum value", "/fields/3/values")]
    [InlineData("enum value changed case", "/fields/3/values")]
    [InlineData("required added to table with rows", "/fields/6/required")]
    [InlineData("existing field made required", "/fields/4/required")]
    [InlineData("existing field made unique", "/fields/4/unique")]
    [InlineData("enum to text", "/fields/3/type")]
    [InlineData("text to enum", "/fields/5/type")]
    public void Incompatible_field_change_is_reported_at_the_field_property(string change, string path)
    {
        var model = BaseModel();
        var changed = change switch
        {
            "changed type" => ChangeOrderField(model, 4, amount => amount with { Type = FieldType.Decimal }),
            "narrowed length" => ChangeOrderField(model, 0, title => title with { MaxLength = 50 }),
            "text to limited length" => ChangeOrderField(model, 5, note => note with { MaxLength = 10 }),
            "changed target" => ChangeOrderField(model, 2, customer => customer with { Target = new EntityReference(_supplierId, "Supplier") }),
            "removed enum value" => ChangeOrderField(model, 3, status => status with { Values = ["draft", "approved"] }),
            "enum value changed case" => ChangeOrderField(model, 3, status => status with { Values = ["Draft", "submitted"] }),
            "required added to table with rows" => ChangeOrder(model, fields => [.. fields, Field("priority", FieldType.Integer, required: true)]),
            "existing field made required" => ChangeOrderField(model, 4, amount => amount with { Required = true }),
            "existing field made unique" => ChangeOrderField(model, 4, amount => amount with { Unique = true }),
            "enum to text" => ChangeOrderField(model, 3, status => status with { Type = FieldType.Text, Values = null }),
            "text to enum" => ChangeOrderField(model, 5, note => note with { Type = FieldType.Enum, Values = ["a"] }),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", path, _orderId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.True(plan.HasErrors);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Removed_enum_values_are_listed_in_one_diagnostic()
    {
        var model = ChangeOrderField(BaseModel(), 3, status => status with { Values = ["draft", "submitted", "approved"] });
        var changed = ChangeOrderField(model, 3, status => status with { Values = ["draft"] });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("/fields/3/values", diagnostic.Path);
        Assert.Contains("'submitted', 'approved'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorded_entity_missing_from_the_model_is_reported_at_the_manifest()
    {
        var model = BaseModel();
        var changed = model with { Entities = [.. model.Entities.Where(entity => entity.Id != _supplierId)] };

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.RemovedEntity, "application.json", "", _supplierId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Entities_recorded_for_another_application_are_not_reported()
    {
        var model = BaseModel();
        var otherId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        var records = Models.Records(model);
        records = records with { Entities = [.. records.Entities, new ProvisionedEntity(otherId, Guid.NewGuid(), EntityNaming.Table(otherId))] };

        var plan = SchemaPlanner.Plan(model, Catalog(model, hasRows: true), records);

        Assert.Empty(plan.Diagnostics);
        Assert.Empty(plan.Statements);
    }

    [Fact]
    public void Entity_id_recorded_for_another_application_is_reported_at_the_entity_id()
    {
        var model = BaseModel();
        var records = Models.Records(model);
        records = records with
        {
            Entities = [.. records.Entities.Select(recorded => recorded.EntityId == _orderId ? recorded with { ApplicationId = Guid.NewGuid() } : recorded)],
        };

        var plan = SchemaPlanner.Plan(model, Catalog(model, hasRows: true), records);

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.EntityOwnedByOtherApplication, "entities/order.json", "/id", _orderId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Several_problems_are_all_reported_in_diagnostic_order()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [fields[0] with { MaxLength = 10 }, fields[1], fields[2], fields[3], fields[4] with { Unique = true }]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Equal(
            [
                (DiagnosticCodes.RemovedField, "/fields"),
                (DiagnosticCodes.IncompatibleFieldChange, "/fields/0/maxLength"),
                (DiagnosticCodes.IncompatibleFieldChange, "/fields/4/unique"),
            ],
            plan.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Empty_catalog_creates_a_child_table_with_owner_and_position_and_a_cascading_foreign_key_last()
    {
        var model = ChildModel();

        var plan = SchemaPlanner.Plan(model, CatalogSnapshot.Empty, ProvisioningRecords.Empty);

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(
            [
                $"""CREATE TABLE "entities"."{OrderTable}" ("id" uuid NOT NULL, "version" bigint NOT NULL DEFAULT 1, "f_title" character varying(100) NOT NULL, CONSTRAINT "pk_{OrderTable}" PRIMARY KEY ("id"))""",
                $"""CREATE TABLE "entities"."{LineTable}" ("id" uuid NOT NULL, "owner_id" uuid NOT NULL, "position" integer NOT NULL, "f_description" text, "f_quantity" bigint NOT NULL, CONSTRAINT "pk_{LineTable}" PRIMARY KEY ("id"))""",
                OwnerForeignKey(LineTable, OrderTable),
            ],
            plan.Statements);
        Assert.Equal(
            [new ProvisionedEntity(_orderId, Models.ApplicationId, OrderTable), new ProvisionedEntity(_lineId, Models.ApplicationId, LineTable)],
            plan.NewEntities);
    }

    [Fact]
    public void Child_collection_added_to_a_table_with_rows_creates_only_the_child_table_and_its_foreign_key()
    {
        var model = BaseModel();
        var line = Line();
        var changed = ChangeOrder(model, fields => [.. fields, Field("lines", FieldType.ChildCollection, target: line)]);
        changed = changed with { Entities = [.. changed.Entities, line] };

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(
            [
                $"""CREATE TABLE "entities"."{LineTable}" ("id" uuid NOT NULL, "owner_id" uuid NOT NULL, "position" integer NOT NULL, "f_description" text, "f_quantity" bigint NOT NULL, CONSTRAINT "pk_{LineTable}" PRIMARY KEY ("id"))""",
                OwnerForeignKey(LineTable, OrderTable),
            ],
            plan.Statements);
        Assert.Equal([new ProvisionedEntity(_lineId, Models.ApplicationId, LineTable)], plan.NewEntities);
    }

    [Fact]
    public void Unchanged_child_table_plans_nothing()
    {
        var model = ChildModel();

        var plan = SchemaPlanner.Plan(model, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Child_table_whose_owner_column_has_no_foreign_key_gets_one()
    {
        var model = ChildModel();
        var catalog = ChangeColumns(
            Catalog(model, hasRows: true),
            LineTable,
            columns => columns.Select(column => column.Name == EntityNaming.OwnerColumn ? column with { ReferencedTable = null } : column));

        var plan = SchemaPlanner.Plan(model, catalog, Models.Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([OwnerForeignKey(LineTable, OrderTable)], plan.Statements);
    }

    [Fact]
    public void Existing_table_that_becomes_a_child_table_is_reported_at_the_owner_target()
    {
        // Supplier already has a table, so it has no owner column.
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields, Field("suppliers", FieldType.ChildCollection, target: Supplier())]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: false), Models.Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.IncompatibleFieldChange, "entities/order.json", "/fields/6/target", _orderId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'Supplier'", diagnostic.Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Child_table_that_changes_its_owner_is_reported_at_the_new_owner_target()
    {
        var model = ChildModel();
        var customer = Customer();
        var changed = model with
        {
            Entities =
            [
                Order(customer) with { Fields = [Field("title", FieldType.Text, required: true, maxLength: 100)] },
                customer with { Fields = [.. customer.Fields, Field("lines", FieldType.ChildCollection, target: Line())] },
                Line(),
            ],
        };
        var catalog = new CatalogSnapshot([.. Catalog(model, hasRows: true).Tables, .. Catalog(Application(customer), hasRows: true).Tables]);
        var records = new ProvisioningRecords([.. Models.Records(model).Entities, .. Models.Records(Application(customer)).Entities], []);

        var plan = SchemaPlanner.Plan(changed, catalog, records);

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.IncompatibleFieldChange, "entities/customer.json", "/fields/1/target", _customerId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Entity_that_stops_being_a_child_reports_its_owner_and_position_columns_as_removed_fields()
    {
        var model = ChildModel();
        var changed = model with
        {
            Entities = [.. model.Entities.Select(entity => entity.Id == _orderId ? entity with { Fields = [entity.Fields[1]] } : entity)],
        };

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Models.Records(model));

        Assert.Equal(
            [(DiagnosticCodes.RemovedField, "entities/line.json"), (DiagnosticCodes.RemovedField, "entities/line.json")],
            plan.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File)));
        Assert.Contains("'owner_id'", plan.Diagnostics[0].Message + plan.Diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Contains("'position'", plan.Diagnostics[0].Message + plan.Diagnostics[1].Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_rename_renames_the_column_and_its_constraints_and_the_recorded_enum_values_before_the_additive_changes()
    {
        var stored = BaseModel();
        var renamed = ChangeOrder(stored, fields => [.. fields.Select(field => field.Name switch
        {
            "code" => field with { Name = "number" },
            "customer" => field with { Name = "buyer" },
            "status" => field with { Name = "state" },
            _ => field,
        }), Field("priority", FieldType.Integer)]);
        var migration = Migration(
            "Order",
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "code", "number", null),
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "customer", "buyer", null),
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "status", "state", null));

        var plan = SchemaPlanner.Plan(renamed, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var alter = $"ALTER TABLE {EntityNaming.QualifiedTable(OrderTable)}";
        Assert.Equal(
            [
                $"{alter} RENAME COLUMN {Quote("f_code")} TO {Quote("f_number")}",
                $"{alter} RENAME CONSTRAINT {Quote(EntityNaming.Unique(OrderTable, "f_code"))} TO {Quote(EntityNaming.Unique(OrderTable, "f_number"))}",
                $"{alter} RENAME COLUMN {Quote("f_customer")} TO {Quote("f_buyer")}",
                $"{alter} RENAME CONSTRAINT {Quote(EntityNaming.ForeignKey(OrderTable, "f_customer"))} TO {Quote(EntityNaming.ForeignKey(OrderTable, "f_buyer"))}",
                $"{alter} RENAME COLUMN {Quote("f_status")} TO {Quote("f_state")}",
                $"{alter} ADD COLUMN {Quote("f_priority")} bigint",
            ],
            plan.Statements);
        Assert.Equal(
            [
                new MigrationRecordChange(MigrationChangeType.RenameField, _orderId, "code", "number"),
                new MigrationRecordChange(MigrationChangeType.RenameField, _orderId, "customer", "buyer"),
                new MigrationRecordChange(MigrationChangeType.RenameField, _orderId, "status", "state"),
            ],
            plan.RecordChanges);
        Assert.Equal([(_orderId, "code"), (_orderId, "customer"), (_orderId, "status")], DestructiveTargets(plan));
        Assert.Empty(plan.NewEnumValues);
    }

    [Fact]
    public void Pending_drop_field_drops_the_column_without_cascade_and_the_recorded_enum_values()
    {
        var stored = BaseModel();
        var dropped = ChangeOrder(stored, fields => [.. fields.Where(field => field.Name is not ("note" or "status"))]);
        var migration = Migration(
            "Order",
            new MigrationChangeModel(MigrationChangeType.DropField, "note", null, null, null),
            new MigrationChangeModel(MigrationChangeType.DropField, "status", null, null, null));

        var plan = SchemaPlanner.Plan(dropped, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal(
            [
                $"ALTER TABLE {EntityNaming.QualifiedTable(OrderTable)} DROP COLUMN {Quote("f_note")}",
                $"ALTER TABLE {EntityNaming.QualifiedTable(OrderTable)} DROP COLUMN {Quote("f_status")}",
            ],
            plan.Statements);
        Assert.Equal(
            [
                new MigrationRecordChange(MigrationChangeType.DropField, _orderId, "note", null),
                new MigrationRecordChange(MigrationChangeType.DropField, _orderId, "status", null),
            ],
            plan.RecordChanges);
        Assert.Equal([(_orderId, "note"), (_orderId, "status")], DestructiveTargets(plan));
    }

    [Fact]
    public void Pending_drop_entity_drops_the_child_table_first_without_cascade_and_records_both_entities()
    {
        var stored = ChildModel();
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.DropEntity, null, null, null, null));

        var plan = SchemaPlanner.Plan(Application(), Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"DROP TABLE {EntityNaming.QualifiedTable(LineTable)}", $"DROP TABLE {EntityNaming.QualifiedTable(OrderTable)}"], plan.Statements);
        Assert.Equal(
            [
                new MigrationRecordChange(MigrationChangeType.DropEntity, _lineId, null, null),
                new MigrationRecordChange(MigrationChangeType.DropEntity, _orderId, null, null),
            ],
            plan.RecordChanges);
        Assert.Equal([(_lineId, null), (_orderId, null)], DestructiveTargets(plan));
    }

    [Fact]
    public void Pending_changes_that_do_not_fit_the_stored_schema_are_skipped_and_what_is_left_is_still_reported()
    {
        var stored = BaseModel();
        var removed = ChangeOrder(stored, fields => [.. fields.Where(field => field.Name != "note")]);
        var migration = Migration(
            "Order",
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "missing", "other", null),
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "note", "amount", null),
            new MigrationChangeModel(MigrationChangeType.DropField, "missing", null, null, null));

        var plan = SchemaPlanner.Plan(removed, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal((DiagnosticCodes.RemovedField, "entities/order.json"), (diagnostic.Code, diagnostic.File));
        AssertNothingPlanned(plan);
        Assert.Empty(plan.RecordChanges);
        Assert.Empty(plan.DestructiveChanges);
    }

    [Fact]
    public void Pending_change_of_an_entity_without_a_stored_table_is_skipped()
    {
        var model = BaseModel();
        var migration = Migration(
            "Order",
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "note", "remark", null),
            new MigrationChangeModel(MigrationChangeType.DropEntity, null, null, null, null));

        var plan = SchemaPlanner.Plan(model, CatalogSnapshot.Empty, ProvisioningRecords.Empty, [migration], StoredIds(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Empty(plan.RecordChanges);
        Assert.Empty(plan.DestructiveChanges);
        Assert.Equal(model.Entities.Count, plan.Statements.Count(statement => statement.StartsWith("CREATE TABLE", StringComparison.Ordinal)));
        Assert.DoesNotContain(plan.Statements, statement => statement.Contains("RENAME", StringComparison.Ordinal) || statement.StartsWith("DROP", StringComparison.Ordinal));
    }

    [Fact]
    public void Pending_convert_of_a_changed_type_stages_the_value_casts_it_and_changes_the_column_type()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 5, field => field with { Type = FieldType.Integer });
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Convert, "note", null, null, "note"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        Assert.Equal(
            [
                $"ALTER TABLE {table} ADD COLUMN {Quote("axis_convert")} bigint",
                $"UPDATE {table} SET {Quote("axis_convert")} = ({Quote("f_note")})::bigint, {Quote("version")} = {Quote("version")} + 1",
                $"ALTER TABLE {table} ALTER COLUMN {Quote("f_note")} TYPE bigint USING {Quote("axis_convert")}",
                $"ALTER TABLE {table} DROP COLUMN {Quote("axis_convert")}",
            ],
            plan.Statements);
        Assert.All(plan.Commands, command => Assert.Equal(new ConvertTarget(migration.Id, "migrations/0001-order.json", 0, "note", false), command.Convert));
        Assert.Equal([(_orderId, "note")], DestructiveTargets(plan));
    }

    [Fact]
    public void Pending_convert_to_a_limited_text_casts_to_text_so_a_long_value_fails_instead_of_truncating()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 5, field => field with { MaxLength = 5 });
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Convert, "note", null, null, "note"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        Assert.Equal($"ALTER TABLE {table} ADD COLUMN {Quote("axis_convert")} character varying(5)", plan.Statements[0]);
        Assert.Contains("::text", plan.Statements[1], StringComparison.Ordinal);
        Assert.Equal($"ALTER TABLE {table} ALTER COLUMN {Quote("f_note")} TYPE character varying(5) USING {Quote("axis_convert")}", plan.Statements[2]);
    }

    [Fact]
    public void Pending_convert_of_removed_enum_values_updates_only_rows_holding_them_and_records_the_removal()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 3, field => field with { Values = ["draft"] });
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Convert, "status", null, null, "'draft'"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        var statement = Assert.Single(plan.Statements);
        Assert.Equal(
            $"WITH converted AS (UPDATE {table} SET {Quote("f_status")} = @f0, {Quote("version")} = {Quote("version")} + 1 WHERE {Quote("f_status")} IN (@r0) RETURNING {Quote("f_status")}) "
            + $"SELECT count(*) FROM converted WHERE {Quote("f_status")} IS NOT NULL AND {Quote("f_status")} NOT IN (@k0)",
            statement);
        var command = plan.Commands[0];
        Assert.Equal(
            [
                new SqlValue("f0", ExpressionTypeKind.Text, "draft"),
                new SqlValue("r0", ExpressionTypeKind.Text, "submitted"),
                new SqlValue("k0", ExpressionTypeKind.Text, "draft"),
            ],
            command.Parameters);
        Assert.Equal(new ConvertTarget(migration.Id, "migrations/0001-order.json", 0, "status", true), command.Convert);
        var change = Assert.Single(plan.RecordChanges);
        Assert.Equal((MigrationChangeType.Convert, _orderId, "status"), (change.Type, change.EntityId, change.Field));
        Assert.Equal(["submitted"], change.Values);
        Assert.Empty(plan.NewEnumValues);
    }

    [Fact]
    public void Removed_enum_values_without_a_convert_are_still_reported()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 3, field => field with { Values = ["draft"] });

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal((DiagnosticCodes.IncompatibleFieldChange, "/fields/3/values"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Pending_convert_on_a_child_entity_is_reported_as_not_applied_and_plans_nothing()
    {
        var stored = ChildModel();
        var migration = Migration("Line", new MigrationChangeModel(MigrationChangeType.Convert, "description", null, null, "description"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MigrationChangeNotApplied, "migrations/0001-order.json", "/changes/0/type", migration.Id),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_convert_of_a_field_a_computed_field_reads_is_reported_as_not_applied_and_plans_nothing()
    {
        var amount = Field("amount", FieldType.Integer);
        var stored = ChangeOrder(
            BaseModel(),
            fields => [.. fields.Where(field => field.Name != "amount"), amount, Computed(Field("doubled", FieldType.Integer), "amount * 2", [amount])]);
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Convert, "amount", null, null, "amount"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(DiagnosticCodes.MigrationChangeNotApplied, diagnostic.Code);
        Assert.Contains("doubled", diagnostic.Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_fill_of_an_existing_nullable_column_plans_the_fill_then_not_null_when_the_field_becomes_required()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 4, field => field with { Required = true });
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "amount", null, null, "0"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        Assert.Equal(
            [
                $"WITH filled AS (UPDATE {table} SET {Quote("f_amount")} = @f0, {Quote("version")} = {Quote("version")} + 1 WHERE {Quote("f_amount")} IS NULL RETURNING {Quote("f_amount")}) SELECT count(*) FROM filled WHERE {Quote("f_amount")} IS NULL",
                $"ALTER TABLE {table} ALTER COLUMN {Quote("f_amount")} SET NOT NULL",
            ],
            plan.Statements);
        var fill = plan.Commands[0];
        Assert.Equal([new SqlValue("f0", ExpressionTypeKind.Integer, 0L)], fill.Parameters);
        Assert.Equal(new FillTarget(migration.Id, "migrations/0001-order.json", 0, "amount"), fill.Fill);
        Assert.Empty(plan.DestructiveChanges);
        Assert.Null(plan.Commands[1].Fill);
    }

    [Fact]
    public void Pending_fill_of_a_missing_required_enum_column_adds_it_without_not_null_then_fills_it_then_makes_it_not_null()
    {
        var stored = BaseModel();
        var release = ChangeOrder(stored, fields => [.. fields, Field("level", FieldType.Enum, required: true, values: ["low", "high"])]);
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "level", null, null, "'low'"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        Assert.Equal(
            [
                $"ALTER TABLE {table} ADD COLUMN {Quote("f_level")} text",
                $"WITH filled AS (UPDATE {table} SET {Quote("f_level")} = @f0, {Quote("version")} = {Quote("version")} + 1 WHERE {Quote("f_level")} IS NULL RETURNING {Quote("f_level")}) SELECT count(*) FROM filled WHERE {Quote("f_level")} IS NULL",
                $"ALTER TABLE {table} ALTER COLUMN {Quote("f_level")} SET NOT NULL",
            ],
            plan.Statements);
        Assert.Equal(
            [new ProvisionedEnumValue(_orderId, "level", "low"), new ProvisionedEnumValue(_orderId, "level", "high")],
            plan.NewEnumValues);
    }

    [Fact]
    public void Pending_fill_of_a_missing_unique_column_adds_its_constraint_after_the_fill()
    {
        var stored = BaseModel();
        var release = ChangeOrder(stored, fields => [.. fields, Field("tag", FieldType.Text, unique: true, maxLength: 10)]);
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "tag", null, null, "code"));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        var table = EntityNaming.QualifiedTable(OrderTable);
        Assert.Equal(
            [
                $"ALTER TABLE {table} ADD COLUMN {Quote("f_tag")} character varying(10)",
                $"WITH filled AS (UPDATE {table} SET {Quote("f_tag")} = {Quote("f_code")}, {Quote("version")} = {Quote("version")} + 1 WHERE {Quote("f_tag")} IS NULL RETURNING {Quote("f_tag")}) SELECT count(*) FROM filled WHERE {Quote("f_tag")} IS NULL",
                $"ALTER TABLE {table} ADD CONSTRAINT {Quote(EntityNaming.Unique(OrderTable, "f_tag"))} UNIQUE ({Quote("f_tag")})",
            ],
            plan.Statements);
    }

    [Fact]
    public void Pending_fill_value_that_names_a_field_without_a_column_gives_an_unknown_field_diagnostic_at_the_value()
    {
        var stored = BaseModel();
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "note", null, null, "missing"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownMigrationField, "migrations/0001-order.json", "/changes/0/value"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_fill_of_a_field_the_release_no_longer_has_and_the_table_lacks_is_skipped()
    {
        var stored = BaseModel();
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "gone", null, null, "0"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        Assert.Empty(plan.Diagnostics);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Existing_required_field_without_a_fill_still_cannot_become_required()
    {
        var stored = BaseModel();
        var release = ChangeOrderField(stored, 4, field => field with { Required = true });

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal((DiagnosticCodes.IncompatibleFieldChange, "/fields/4/required"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Pending_fill_on_a_child_entity_is_reported_as_not_applied_and_plans_nothing()
    {
        var stored = ChildModel();
        var migration = Migration("Line", new MigrationChangeModel(MigrationChangeType.Fill, "description", null, null, "'x'"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MigrationChangeNotApplied, "migrations/0001-order.json", "/changes/0/type", migration.Id),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_fill_of_a_field_a_computed_field_reads_is_reported_as_not_applied_and_plans_nothing()
    {
        var amount = Field("amount", FieldType.Integer);
        var stored = ChangeOrder(
            BaseModel(),
            fields => [.. fields.Where(field => field.Name != "amount"), amount, Computed(Field("doubled", FieldType.Integer), "amount * 2", [amount])]);
        var migration = Migration("Order", new MigrationChangeModel(MigrationChangeType.Fill, "amount", null, null, "0"));

        var plan = SchemaPlanner.Plan(stored, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MigrationChangeNotApplied, "migrations/0001-order.json", "/changes/0/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("doubled", diagnostic.Message, StringComparison.Ordinal);
        AssertNothingPlanned(plan);
    }

    [Fact]
    public void Pending_fill_followed_by_a_rename_of_a_field_a_computed_field_reads_is_reported_as_not_applied_and_plans_nothing()
    {
        var stored = BaseModel();
        var gross = Field("gross", FieldType.Integer);
        var release = ChangeOrder(BaseModel(), fields => [.. fields.Where(field => field.Name != "amount"), gross, Computed(Field("doubled", FieldType.Integer), "gross * 2", [gross])]);
        var migration = Migration(
            "Order",
            new MigrationChangeModel(MigrationChangeType.Fill, "amount", null, null, "0"),
            new MigrationChangeModel(MigrationChangeType.RenameField, null, "amount", "gross", null));

        var plan = SchemaPlanner.Plan(release, Catalog(stored, hasRows: true), Models.Records(stored), [migration], StoredIds(stored));

        var diagnostic = Assert.Single(plan.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.MigrationChangeNotApplied);
        Assert.Equal("/changes/0/type", diagnostic.Path);
        Assert.Contains("doubled", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Statements, statement => statement.Contains("UPDATE", StringComparison.Ordinal));
    }

    private static string Quote(string identifier) => EntityNaming.Quote(identifier);

    private static MigrationModel Migration(string entity, params MigrationChangeModel[] changes) =>
        new()
        {
            Id = Guid.Parse("55555555-5555-4555-8555-555555555555"),
            Name = "Change",
            File = "migrations/0001-order.json",
            Entity = entity,
            Changes = changes,
            ContentHash = new string('a', 64),
        };

    private static Dictionary<string, Guid> StoredIds(ApplicationModel model) =>
        model.Entities.ToDictionary(entity => entity.Name, entity => entity.Id, StringComparer.OrdinalIgnoreCase);

    private static EntityModel Line() =>
        Entity(
            _lineId,
            "Line",
            "entities/line.json",
            Field("description", FieldType.Text),
            Field("quantity", FieldType.Integer, required: true));

    // Order owns Line through the child collection between its two column fields.
    private static ApplicationModel ChildModel() =>
        Application(
            Entity(
                _orderId,
                "Order",
                "entities/order.json",
                Field("lines", FieldType.ChildCollection, target: Line()),
                Field("title", FieldType.Text, required: true, maxLength: 100)),
            Line());

    private static EntityModel Customer() =>
        Entity(_customerId, "Customer", "entities/customer.json", Field("name", FieldType.Text, required: true));

    private static EntityModel Supplier() =>
        Entity(_supplierId, "Supplier", "entities/supplier.json", Field("name", FieldType.Text));

    private static EntityModel Order(EntityModel customer) =>
        Entity(
            _orderId,
            "Order",
            "entities/order.json",
            Field("title", FieldType.Text, required: true, maxLength: 100),
            Field("code", FieldType.Text, unique: true, maxLength: 20),
            Field("customer", FieldType.Reference, target: customer),
            Field("status", FieldType.Enum, values: ["draft", "submitted"]),
            Field("amount", FieldType.Integer),
            Field("note", FieldType.Text));

    private static ApplicationModel BaseModel() => Application(Customer(), Order(Customer()), Supplier());

    private static ApplicationModel ChangeOrder(ApplicationModel model, Func<IReadOnlyList<FieldModel>, IReadOnlyList<FieldModel>> change) =>
        model with { Entities = [.. model.Entities.Select(entity => entity.Id == _orderId ? entity with { Fields = change(entity.Fields) } : entity)] };

    private static ApplicationModel ChangeOrderField(ApplicationModel model, int index, Func<FieldModel, FieldModel> change) =>
        ChangeOrder(model, fields => [.. fields.Select((field, i) => i == index ? change(field) : field)]);

    private static CatalogSnapshot ChangeOrderColumns(CatalogSnapshot catalog, Func<IEnumerable<CatalogColumn>, IEnumerable<CatalogColumn>> change) =>
        ChangeColumns(catalog, OrderTable, change);

    private static CatalogSnapshot ChangeColumns(CatalogSnapshot catalog, string tableName, Func<IEnumerable<CatalogColumn>, IEnumerable<CatalogColumn>> change) =>
        new([.. catalog.Tables.Select(table => table.Name == tableName ? table with { Columns = [.. change(table.Columns)] } : table)]);

    private static string OwnerForeignKey(string table, string ownerTable) =>
        $"""ALTER TABLE "entities"."{table}" ADD CONSTRAINT "{EntityNaming.ForeignKey(table, "owner_id")}" FOREIGN KEY ("owner_id") REFERENCES "entities"."{ownerTable}" ("id") ON DELETE CASCADE""";

    private static string ForeignKey(string table, string column, string targetTable) =>
        $"""ALTER TABLE "entities"."{table}" ADD CONSTRAINT "{EntityNaming.ForeignKey(table, column)}" FOREIGN KEY ("{column}") REFERENCES "entities"."{targetTable}" ("id")""";

    private static void AssertNothingPlanned(SchemaPlan plan)
    {
        Assert.Empty(plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.NewEnumValues);
    }
}
