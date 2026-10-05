using System.Text;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Naming;
using Axis.Data.Schema;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class EntityNamingTests
{
    private const int MaxIdentifierBytes = 63;

    private static readonly Guid _entityId = Guid.Parse("4B6F0C1E-6A0E-4C47-9A53-0F5F8F8B1A01");

    [Fact]
    public void Names_for_60_character_entity_and_field_names_fit_in_63_bytes()
    {
        var entityName = "E" + new string('x', 59);
        var fieldName = "F" + new string('y', 59);
        var entity = Entity(_entityId, entityName, "entity.json", Field(fieldName, FieldType.Text));

        var table = EntityNaming.Table(entity.Id);
        var column = EntityNaming.Column(entity.Fields[0].Name);

        Assert.Equal("e_4b6f0c1e6a0e4c479a530f5f8f8b1a01", table);
        Assert.Equal("f_f" + new string('y', 59), column);
        Assert.All(
            [table, column, EntityNaming.PrimaryKey(table), EntityNaming.Unique(table, column), EntityNaming.ForeignKey(table, column)],
            name => Assert.InRange(Encoding.UTF8.GetByteCount(name), 1, MaxIdentifierBytes));
    }

    [Fact]
    public void Constraint_names_hash_the_column_and_differ_per_column_and_kind()
    {
        var table = EntityNaming.Table(_entityId);

        Assert.Matches("^uq_e_4b6f0c1e6a0e4c479a530f5f8f8b1a01_[0-9a-f]{16}$", EntityNaming.Unique(table, "f_code"));
        Assert.Matches("^fk_e_4b6f0c1e6a0e4c479a530f5f8f8b1a01_[0-9a-f]{16}$", EntityNaming.ForeignKey(table, "f_code"));
        Assert.Equal(EntityNaming.Unique(table, "f_code")[^16..], EntityNaming.ForeignKey(table, "f_code")[^16..]);
        Assert.NotEqual(EntityNaming.Unique(table, "f_code"), EntityNaming.Unique(table, "f_name"));
        Assert.Equal("pk_e_4b6f0c1e6a0e4c479a530f5f8f8b1a01", EntityNaming.PrimaryKey(table));
    }

    [Fact]
    public void Column_names_ignore_the_letter_case_of_the_field_name()
    {
        Assert.Equal("f_ordernumber", EntityNaming.Column("OrderNumber"));
        Assert.Equal(EntityNaming.Column("orderNumber"), EntityNaming.Column("ORDERNUMBER"));
    }

    [Fact]
    public void Identifiers_are_double_quoted_with_embedded_quotes_doubled()
    {
        Assert.Equal("\"f_code\"", EntityNaming.Quote("f_code"));
        Assert.Equal("\"a\"\"b\"", EntityNaming.Quote("a\"b"));
        Assert.Equal("\"entities\".\"e_1\"", EntityNaming.QualifiedTable("e_1"));
    }

    [Fact]
    public void The_same_model_gives_the_same_names_and_a_label_change_does_not_change_them()
    {
        var customer = Entity(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Customer", "customer.json", Field("name", FieldType.Text, unique: true));
        var order = Entity(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Order",
            "order.json",
            Field("customer", FieldType.Reference, target: customer),
            Field("code", FieldType.Text, unique: true));
        var model = Application(customer, order);
        var relabelled = Application(
            customer with { Label = new TextReference("customer.renamed") },
            order with
            {
                Label = new TextReference("order.renamed"),
                Fields = [.. order.Fields.Select(field => field with { Label = new TextReference($"order.{field.Name}.renamed") })],
            });

        var first = SchemaPlanner.Plan(model, CatalogSnapshot.Empty, ProvisioningRecords.Empty);
        var second = SchemaPlanner.Plan(model, CatalogSnapshot.Empty, ProvisioningRecords.Empty);
        var afterLabelChange = SchemaPlanner.Plan(relabelled, CatalogSnapshot.Empty, ProvisioningRecords.Empty);

        Assert.NotEmpty(first.Statements);
        Assert.Equal(first.Statements, second.Statements);
        Assert.Equal(first.Statements, afterLabelChange.Statements);
        Assert.Equal(first.NewEntities, afterLabelChange.NewEntities);
    }
}
