using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Naming;
using Axis.Data.Schema;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class SchemaPlannerTests
{
    private const string CustomerTable = "e_22222222222242228222222222222222";
    private const string OrderTable = "e_11111111111141118111111111111111";

    private static readonly Guid _customerId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid _orderId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid _supplierId = Guid.Parse("33333333-3333-4333-8333-333333333333");

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
                $"""CREATE TABLE "entities"."{CustomerTable}" ("id" uuid NOT NULL, "f_name" character varying(200) NOT NULL, "f_lastorder" uuid, CONSTRAINT "pk_{CustomerTable}" PRIMARY KEY ("id"))""",
                $"""CREATE TABLE "entities"."{OrderTable}" ("id" uuid NOT NULL, "f_code" character varying(20) NOT NULL, "f_customer" uuid NOT NULL, "f_parent" uuid, "f_total" numeric(18,2), "f_quantity" bigint, "f_paid" boolean, "f_due" date, "f_placedat" timestamp with time zone, "f_notes" text, "f_status" text NOT NULL, CONSTRAINT "pk_{OrderTable}" PRIMARY KEY ("id"), CONSTRAINT "{EntityNaming.Unique(OrderTable, "f_code")}" UNIQUE ("f_code"))""",
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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{OrderTable}" ADD COLUMN "f_priority" bigint"""], plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.NewEnumValues);
    }

    [Fact]
    public void Added_required_unique_reference_on_a_table_without_rows_adds_the_column_and_its_constraints()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [.. fields, Field("billTo", FieldType.Reference, required: true, unique: true, target: Customer())]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: false), Records(model));

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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([$"""ALTER TABLE "entities"."{OrderTable}" {statement}"""], plan.Statements);
    }

    [Fact]
    public void Dropping_unique_drops_the_unique_constraint()
    {
        var model = BaseModel();
        var changed = ChangeOrderField(model, 1, code => code with { Unique = false });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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

        var plan = SchemaPlanner.Plan(model, catalog, Records(model));

        Assert.Empty(plan.Diagnostics);
        Assert.Equal([ForeignKey(OrderTable, "f_customer", CustomerTable)], plan.Statements);
    }

    [Fact]
    public void Added_enum_value_is_recorded_without_statements()
    {
        var model = BaseModel();
        var changed = ChangeOrderField(model, 3, status => status with { Values = ["draft", "submitted", "Draft"] });

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

        var diagnostic = Assert.Single(plan.Diagnostics);
        Assert.Equal("/fields/3/values", diagnostic.Path);
        Assert.Contains("'submitted', 'approved'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorded_entity_missing_from_the_model_is_reported_at_the_manifest()
    {
        var model = BaseModel();
        var changed = model with { Entities = [.. model.Entities.Where(entity => entity.Id != _supplierId)] };

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

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
        var records = Records(model);
        records = records with { Entities = [.. records.Entities, new ProvisionedEntity(otherId, Guid.NewGuid(), EntityNaming.Table(otherId))] };

        var plan = SchemaPlanner.Plan(model, Catalog(model, hasRows: true), records);

        Assert.Empty(plan.Diagnostics);
        Assert.Empty(plan.Statements);
    }

    [Fact]
    public void Several_problems_are_all_reported_in_diagnostic_order()
    {
        var model = BaseModel();
        var changed = ChangeOrder(model, fields => [fields[0] with { MaxLength = 10 }, fields[1], fields[2], fields[3], fields[4] with { Unique = true }]);

        var plan = SchemaPlanner.Plan(changed, Catalog(model, hasRows: true), Records(model));

        Assert.Equal(
            [
                (DiagnosticCodes.RemovedField, "/fields"),
                (DiagnosticCodes.IncompatibleFieldChange, "/fields/0/maxLength"),
                (DiagnosticCodes.IncompatibleFieldChange, "/fields/4/unique"),
            ],
            plan.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        AssertNothingPlanned(plan);
    }

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

    private static string ForeignKey(string table, string column, string targetTable) =>
        $"""ALTER TABLE "entities"."{table}" ADD CONSTRAINT "{EntityNaming.ForeignKey(table, column)}" FOREIGN KEY ("{column}") REFERENCES "entities"."{targetTable}" ("id")""";

    private static void AssertNothingPlanned(SchemaPlan plan)
    {
        Assert.Empty(plan.Statements);
        Assert.Empty(plan.NewEntities);
        Assert.Empty(plan.NewEnumValues);
    }
}
