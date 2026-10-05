using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class ApplicationCompilerTests
{
    private const string Manifest = """
        { "id": "0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01", "kind": "application", "name": "Sample", "formatVersion": 1 }
        """;

    private const string OrderId = "11111111-1111-4111-8111-111111111111";

    [Fact]
    public void Valid_fixture_compiles_into_a_typed_model_with_resolved_references()
    {
        var result = ApplicationCompiler.Compile(Fixture("valid-app"));

        Assert.Empty(result.Diagnostics);
        Assert.False(result.HasErrors);
        Assert.NotNull(result.Model);
        Assert.Equal("PurchaseRequests", result.Model.Manifest.Name);
        Assert.Equal(["Department", "PurchaseRequest", "Supplier"], result.Model.Entities.Select(entity => entity.Name));

        Assert.True(result.Model.TryGetEntity("purchaserequest", out var purchaseRequest));
        Assert.Equal(
            [FieldType.Text, FieldType.Reference, FieldType.Reference, FieldType.Decimal, FieldType.Date, FieldType.Enum],
            purchaseRequest.Fields.Select(field => field.Type));

        Assert.True(purchaseRequest.TryGetField("department", out var department));
        Assert.Equal(new EntityReference(Guid.Parse("6a1e4f2b-3c4d-4e5f-8a9b-0c1d2e3f4a02"), "Department"), department.Target);
        Assert.True(department.Required);
        Assert.False(department.Unique);

        Assert.True(purchaseRequest.TryGetField("supplier", out var supplier));
        Assert.Equal(new EntityReference(Guid.Parse("7b2f5a3c-4d5e-4f6a-9b0c-1d2e3f4a5b03"), "Supplier"), supplier.Target);
        Assert.False(supplier.Required);
        Assert.False(supplier.Unique);

        Assert.True(purchaseRequest.TryGetField("total", out var total));
        Assert.Equal((18, 2), (total.Precision, total.Scale));
        Assert.Null(total.Target);

        Assert.True(purchaseRequest.TryGetField("status", out var status));
        Assert.Equal(["draft", "submitted", "approved", "rejected"], status.Values);

        Assert.True(result.Model.TryGetEntity("Department", out var departmentEntity));
        Assert.True(departmentEntity.TryGetField("code", out var code));
        Assert.Equal((FieldType.Text, true, true, 20), (code.Type, code.Required, code.Unique, code.MaxLength));

        Assert.True(result.Model.TryGetEntity("Supplier", out var supplierEntity));
        Assert.Equal([FieldType.Text, FieldType.Boolean], supplierEntity.Fields.Select(field => field.Type));
        Assert.Equal("entities/supplier.json", supplierEntity.File);
    }

    [Theory]
    [InlineData(61, 1, "/name")]
    [InlineData(1, 61, "/fields/0/name")]
    public void Entity_or_field_name_longer_than_60_characters_is_a_schema_violation(int entityNameLength, int fieldNameLength, string path)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity(Name(entityNameLength), $$"""{ "name": "{{Name(fieldNameLength)}}", "type": "text" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Application_name_longer_than_60_characters_is_a_schema_violation()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest.Replace("\"Sample\"", $"\"{Name(61)}\"", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "application.json", "/name"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Names_of_60_characters_compile()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest.Replace("\"Sample\"", $"\"{Name(60)}\"", StringComparison.Ordinal))
            .With("entities/order.json", Entity(Name(60), $$"""{ "name": "{{Name(60)}}", "type": "text" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var entity = Assert.Single(result.Model.Entities);
        Assert.Equal((Name(60), Name(60), "entities/order.json"), (entity.Name, Assert.Single(entity.Fields).Name, entity.File));
    }

    [Fact]
    public void Reference_to_an_unknown_entity_is_reported_at_the_target()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", Entity("Order", """{ "name": "customer", "type": "reference", "target": "Customer" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownReferenceTarget, "entities/order.json", "/fields/0/target", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Reference_targets_resolve_ignoring_letter_case_and_to_the_entity_itself()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("customer.json", Entity("Customer", """{ "name": "name", "type": "text" }""", "22222222-2222-4222-8222-222222222222"))
            .With("order.json", Entity(
                "Order",
                """
                { "name": "customer", "type": "reference", "target": "CUSTOMER" },
                { "name": "parent", "type": "reference", "target": "Order" }
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(new EntityReference(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Customer"), order.Fields[0].Target);
        Assert.Equal(new EntityReference(Guid.Parse(OrderId), "Order"), order.Fields[1].Target);
    }

    [Theory]
    [InlineData("""{ "name": "f", "type": "integer", "maxLength": 10 }""", "/fields/0/maxLength")]
    [InlineData("""{ "name": "f", "type": "text", "precision": 10 }""", "/fields/0/precision")]
    [InlineData("""{ "name": "f", "type": "text", "scale": 2 }""", "/fields/0/scale")]
    [InlineData("""{ "name": "f", "type": "text", "target": "Unknown" }""", "/fields/0/target")]
    [InlineData("""{ "name": "f", "type": "integer", "values": ["a"] }""", "/fields/0/values")]
    [InlineData("""{ "name": "f", "type": "boolean", "maxLength": 0 }""", "/fields/0/maxLength")]
    [InlineData("""{ "name": "f", "type": "reference", "target": "Order", "values": ["a"] }""", "/fields/0/values")]
    public void Property_on_a_type_it_does_not_fit_is_reported_at_the_property(string field, string path)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", field));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse(OrderId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "f", "type": "reference" }""", "'target'")]
    [InlineData("""{ "name": "f", "type": "enum" }""", "'values'")]
    public void Reference_without_target_and_enum_without_values_are_reported_at_the_field(string field, string property)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", field));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTypeProperty, "order.json", "/fields/0"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(property, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "f", "type": "text", "maxLength": 0 }""", "/fields/0/maxLength")]
    [InlineData("""{ "name": "f", "type": "text", "maxLength": 10485761 }""", "/fields/0/maxLength")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 0 }""", "/fields/0/precision")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 1001 }""", "/fields/0/precision")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 5, "scale": 6 }""", "/fields/0/scale")]
    [InlineData("""{ "name": "f", "type": "decimal", "scale": 2 }""", "/fields/0/scale")]
    public void Constraint_value_outside_what_storage_accepts_is_reported_at_the_constraint(string field, string path)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", field));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidConstraint, "order.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "f", "type": "text", "maxLength": 1 }""")]
    [InlineData("""{ "name": "f", "type": "text", "maxLength": 10485760 }""")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 1 }""")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 1000 }""")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 10, "scale": 0 }""")]
    [InlineData("""{ "name": "f", "type": "decimal", "precision": 10, "scale": 10 }""")]
    public void Constraint_values_at_the_storage_limits_compile(string field)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", field));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("integer")]
    [InlineData("decimal")]
    [InlineData("boolean")]
    [InlineData("date")]
    [InlineData("date-time")]
    [InlineData("enum", """, "values": ["a"]""")]
    [InlineData("reference", """, "target": "Order" """)]
    public void Required_and_unique_apply_to_every_field_type(string type, string extra = "")
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", $$"""{ "name": "f", "type": "{{type}}", "required": true, "unique": true{{extra}} }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var field = Assert.Single(Assert.Single(result.Model.Entities).Fields);
        Assert.Equal((FieldTypes.Parse(type), true, true), (field.Type, field.Required, field.Unique));
    }

    [Fact]
    public void Field_names_that_differ_only_by_letter_case_are_duplicates()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity(
                "Order",
                """
                { "name": "code", "type": "text" },
                { "name": "title", "type": "text" },
                { "name": "Code", "type": "integer" }
                """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateFieldName, "order.json", "/fields/2/name", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'code'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("/fields/0", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""["draft", "draft"]""")]
    public void Enum_values_must_be_a_non_empty_list_without_duplicates(string values)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", $$"""{ "name": "status", "type": "enum", "values": {{values}} }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "order.json", "/fields/0/values"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Entity_with_several_problems_is_reported_in_one_pass_merged_with_loader_diagnostics()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/a-malformed.json", "{ \"kind\": ")
            .With("entities/b-order.json", Entity(
                "Order",
                """
                { "name": "code", "type": "text" },
                { "name": "Code", "type": "integer", "maxLength": 10 },
                { "name": "customer", "type": "reference", "target": "Customer" },
                { "name": "status", "type": "enum" },
                { "name": "total", "type": "decimal", "precision": 1001, "scale": 2 }
                """))
            .With("entities/c-invoice.json", Entity("Invoice", """{ "name": "amount", "type": "money" }""", "22222222-2222-4222-8222-222222222222"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.True(result.HasErrors);
        Assert.Null(result.Model);
        Assert.Equal(
            [
                (DiagnosticCodes.InvalidJson, "entities/a-malformed.json", ""),
                (DiagnosticCodes.InvalidConstraint, "entities/b-order.json", "/fields/1/maxLength"),
                (DiagnosticCodes.DuplicateFieldName, "entities/b-order.json", "/fields/1/name"),
                (DiagnosticCodes.UnknownReferenceTarget, "entities/b-order.json", "/fields/2/target"),
                (DiagnosticCodes.MissingTypeProperty, "entities/b-order.json", "/fields/3"),
                (DiagnosticCodes.InvalidConstraint, "entities/b-order.json", "/fields/4/precision"),
                (DiagnosticCodes.SchemaViolation, "entities/c-invoice.json", "/fields/0/type"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.All(result.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public void Loader_errors_alone_leave_no_model()
    {
        using var folder = new TemporaryFolder()
            .With("order.json", Entity("Order", """{ "name": "number", "type": "text" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCodes.ManifestMissing, diagnostic.Code);
        Assert.Null(result.Model);
    }

    private static string Entity(string name, string fields, string id = OrderId) =>
        $$"""{ "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1, "fields": [{{fields}}] }""";

    private static string Name(int length) => "N" + new string('a', length - 1);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
