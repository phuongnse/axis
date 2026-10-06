using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

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
        Assert.Equal("name", department.TargetDisplayField);
        Assert.True(department.Required);
        Assert.False(department.Unique);

        Assert.True(purchaseRequest.TryGetField("supplier", out var supplier));
        Assert.Equal(new EntityReference(Guid.Parse("7b2f5a3c-4d5e-4f6a-9b0c-1d2e3f4a5b03"), "Supplier"), supplier.Target);
        Assert.Equal("name", supplier.TargetDisplayField);
        Assert.False(supplier.Required);
        Assert.False(supplier.Unique);

        Assert.True(purchaseRequest.TryGetField("total", out var total));
        Assert.Equal((18, 2), (total.Precision, total.Scale));
        Assert.Null(total.Target);
        Assert.Null(total.TargetDisplayField);

        Assert.True(purchaseRequest.TryGetField("status", out var status));
        Assert.Equal(["draft", "submitted", "approved", "rejected"], status.Values);

        Assert.True(result.Model.TryGetEntity("Department", out var departmentEntity));
        Assert.True(departmentEntity.TryGetField("code", out var code));
        Assert.Equal((FieldType.Text, true, true, 20), (code.Type, code.Required, code.Unique, code.MaxLength));

        Assert.True(result.Model.TryGetEntity("Supplier", out var supplierEntity));
        Assert.Equal([FieldType.Text, FieldType.Boolean], supplierEntity.Fields.Select(field => field.Type));
        Assert.Equal("entities/supplier.json", supplierEntity.File);

        Assert.Equal(("name", "name"), (departmentEntity.DisplayField, supplierEntity.DisplayField));
        Assert.Null(purchaseRequest.DisplayField);
        Assert.Equal(["en", "vi"], result.Model.Texts.Select(text => text.Locale));
        Assert.Equal(["texts/en.json", "texts/vi.json"], result.Model.Texts.Select(text => text.File));
        Assert.Equal("Phòng ban", result.Model.Texts[1].Texts["department.label"]);

        var purchaseRequestReference = new EntityReference(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1a01"), "PurchaseRequest");
        var formPage = new PageReference(Guid.Parse("e7a0f4c3-9d5b-4e1a-9f3c-4b5d6e7f8a08"), "PurchaseRequestForm");
        var tablePage = new PageReference(Guid.Parse("d6f9e3b2-8c4a-4d0f-8e2b-3a4c5d6e7f07"), "PurchaseRequests");
        Assert.Equal([formPage.Name, tablePage.Name], result.Model.Pages.Select(page => page.Name));
        Assert.Equal(
            new WidgetModel(WidgetType.Form, purchaseRequestReference, null),
            Assert.Single(result.Model.Pages[0].Widgets));
        Assert.Equal(
            new WidgetModel(WidgetType.Table, purchaseRequestReference, formPage),
            Assert.Single(result.Model.Pages[1].Widgets));
        Assert.Equal(("pages/purchase-requests.json", "pages.requests.title"), (result.Model.Pages[1].File, result.Model.Pages[1].Title.TextKey));

        var site = Assert.Single(result.Model.Sites);
        Assert.Equal(("Purchasing", "purchasing", "purchasing.title"), (site.Name, site.Path, site.Title.TextKey));
        Assert.Equal(("en", "en"), (site.Locales.Default, site.Locales.Fallback));
        Assert.Equal(["en", "vi"], site.Locales.Available);
        Assert.Equal(new NavigationModel(tablePage, new TextReference("purchasing.nav.requests")), Assert.Single(site.Navigation));
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
        // Another entity file that failed to load does not hide an unknown target.
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/invoice.json", """{ "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Invoice", "formatVersion": 1 }""")
            .With("entities/order.json", Entity("Order", """{ "name": "customer", "type": "reference", "target": "Customer" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [DiagnosticCodes.SchemaViolation, DiagnosticCodes.UnknownReferenceTarget],
            result.Diagnostics.Select(diagnostic => diagnostic.Code));
        var diagnostic = result.Diagnostics[1];
        Assert.Equal(
            (DiagnosticCodes.UnknownReferenceTarget, "entities/order.json", "/fields/0/target", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Reference_to_an_entity_that_failed_to_load_reports_only_that_entity_file()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("customer.json", """{ "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1 }""")
            .With("order.json", Entity("Order", """{ "name": "customer", "type": "reference", "target": "customer" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "customer.json", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == DiagnosticCodes.UnknownReferenceTarget);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Missing_folder_compiles_into_only_the_unlistable_folder_diagnostic()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(Path.GetTempPath(), $"axis-config-{Guid.NewGuid():N}"));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnlistableFolder, "", ""), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Reference_targets_resolve_ignoring_letter_case_and_to_the_entity_itself()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("customer.json", Entity("Customer", """{ "name": "name", "type": "text", "required": true }""", "22222222-2222-4222-8222-222222222222", displayField: "name"))
            .With("order.json", Entity(
                "Order",
                """
                { "name": "customer", "type": "reference", "target": "CUSTOMER" },
                { "name": "parent", "type": "reference", "target": "Order" },
                { "name": "number", "type": "text", "required": true }
                """,
                displayField: "number"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Order", out var order));
        Assert.Equal(new EntityReference(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Customer"), order.Fields[0].Target);
        Assert.Equal(new EntityReference(Guid.Parse(OrderId), "Order"), order.Fields[1].Target);
        Assert.Equal(("name", "number"), (order.Fields[0].TargetDisplayField, order.Fields[1].TargetDisplayField));
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
        // The required text field names Order's records, so Order can be a reference target.
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", $$"""{{field}}, { "name": "number", "type": "text", "required": true }""", displayField: "number"));

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
            .With("order.json", Entity(
                "Order",
                $$"""{ "name": "f", "type": "{{type}}", "required": true, "unique": true{{extra}} }, { "name": "number", "type": "text", "required": true }""",
                displayField: "number"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(Assert.Single(result.Model.Entities).TryGetField("f", out var field));
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

    [Fact]
    public void Second_text_file_for_a_locale_ignoring_letter_case_is_a_duplicate_locale()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("texts/en.json", Texts("en", """ "a.label": "A" """, "33333333-3333-4333-8333-333333333333", "TextsEn"))
            .With("texts/english.json", Texts("EN", """ "b.label": "B" """, "44444444-4444-4444-8444-444444444444", "TextsEnglish"));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The duplicate is left out of the drift check, so its other keys are not reported on top.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateLocale, "texts/english.json", "/locale", Guid.Parse("44444444-4444-4444-8444-444444444444")),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'texts/en.json'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Key_that_one_locale_lacks_is_reported_in_that_locale_naming_a_locale_that_has_it()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("texts/en.json", Texts("en", """ "a.label": "A", "b.label": "B" """, "33333333-3333-4333-8333-333333333333", "TextsEn"))
            .With("texts/vi.json", Texts("vi", """ "a.label": "A" """, "44444444-4444-4444-8444-444444444444", "TextsVi"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.LocaleDrift, "texts/vi.json", "/texts", Guid.Parse("44444444-4444-4444-8444-444444444444")),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'b.label'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'en'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "number", "type": "text", "label": { "textKey": "a.label" } }""", "missing.label", "/label/textKey")]
    [InlineData("""{ "name": "number", "type": "text", "label": { "textKey": "missing.label" } }""", "a.label", "/fields/0/label/textKey")]
    public void Label_key_that_no_locale_has_is_reported_at_the_label(string field, string entityTextKey, string path)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", $$"""{ "id": "{{OrderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "label": { "textKey": "{{entityTextKey}}" }, "fields": [{{field}}] }""")
            .With("texts/en.json", Texts("en", """ "a.label": "A", "unused.label": "Unused" """, "33333333-3333-4333-8333-333333333333"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, "order.json", path, Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'missing.label'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Application_label_without_any_text_resource_is_a_missing_key()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest.Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"label\": { \"textKey\": \"sample.label\" }", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTextKey, "application.json", "/label/textKey"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("unknown", "'unknown'")]
    [InlineData("note", "an optional text")]
    [InlineData("count", "a required integer")]
    public void Display_field_that_is_not_a_required_text_field_is_reported(string displayField, string expected)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity(
                "Order",
                """
                { "name": "number", "type": "text", "required": true },
                { "name": "note", "type": "text" },
                { "name": "count", "type": "integer", "required": true }
                """,
                displayField: displayField));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDisplayField, "order.json", "/displayField", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains(expected, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Display_field_matches_ignoring_letter_case_and_keeps_the_declared_name()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity(
                "Order",
                """
                { "name": "orderNumber", "type": "text", "required": true },
                { "name": "parent", "type": "reference", "target": "Order" }
                """,
                displayField: "ORDERNUMBER"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var order = Assert.Single(result.Model.Entities);
        Assert.Equal("orderNumber", order.DisplayField);
        Assert.Equal("orderNumber", order.Fields[1].TargetDisplayField);
    }

    [Fact]
    public void Reference_to_an_entity_without_a_display_field_is_reported_at_the_target()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("customer.json", Entity("Customer", """{ "name": "name", "type": "text", "required": true }""", "22222222-2222-4222-8222-222222222222"))
            .With("order.json", Entity("Order", """{ "name": "customer", "type": "reference", "target": "customer" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ReferenceTargetWithoutDisplayField, "order.json", "/fields/0/target", Guid.Parse(OrderId)),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.Contains("'Customer'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Self_reference_requires_a_display_field()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("order.json", Entity("Order", """{ "name": "parent", "type": "reference", "target": "Order" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ReferenceTargetWithoutDisplayField, "/fields/0/target"), (diagnostic.Code, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Compiling_a_folder_and_its_resources_in_memory_gives_the_same_model_and_content_hash()
    {
        var folder = ApplicationCompiler.Compile(Fixture("valid-app"));
        var memory = ApplicationCompiler.Compile(ReadResources(Fixture("valid-app")));

        Assert.Empty(memory.Diagnostics);
        Assert.NotNull(folder.Model);
        Assert.NotNull(memory.Model);
        ModelAssert.Equal(folder.Model, memory.Model);
        Assert.NotNull(memory.ContentHash);
        Assert.Equal(folder.ContentHash, memory.ContentHash);
        Assert.Equal(folder.Resources, memory.Resources);
    }

    [Fact]
    public void Compiling_an_invalid_folder_and_its_resources_in_memory_gives_the_same_diagnostics()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", Manifest)
            .With("entities/order.json", Entity("Order", """{ "name": "number", "type": "text" }"""))
            .With("entities/invoice.json", Entity("Invoice", """{ "name": "number", "type": "text" }"""))
            .With("entities/repeated.json", """{ "kind": "entity", "kind": "entity" }""");

        var fromFolder = ApplicationCompiler.Compile(folder.Path);
        var fromMemory = ApplicationCompiler.Compile(ReadResources(folder.Path));

        Assert.Contains(fromFolder.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.DuplicateId);
        Assert.Contains(fromFolder.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.InvalidJson);
        Assert.Equal(fromFolder.Diagnostics, fromMemory.Diagnostics);
        Assert.Null(fromFolder.Model);
        Assert.Null(fromMemory.Model);
    }

    [Fact]
    public void Resources_in_memory_with_a_repeated_path_throw()
    {
        IReadOnlyList<ResourceContent> resources =
        [
            new ResourceContent("application.json", Manifest),
            new ResourceContent("application.json", Manifest),
        ];

        Assert.Throws<ArgumentException>(() => ApplicationCompiler.Compile(resources));
    }

    private static string Entity(string name, string fields, string id = OrderId, string? displayField = null) =>
        displayField is null
            ? $$"""{ "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1, "fields": [{{fields}}] }"""
            : $$"""{ "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1, "displayField": "{{displayField}}", "fields": [{{fields}}] }""";

    private static string Texts(string locale, string texts, string id, string name = "Texts") =>
        $$"""{ "id": "{{id}}", "kind": "text", "name": "{{name}}", "formatVersion": 1, "locale": "{{locale}}", "texts": {{{texts}}} }""";

    private static string Name(int length) => "N" + new string('a', length - 1);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Every <c>*.json</c> file of the folder as written, with its relative path and <c>/</c> separators.</summary>
    private static List<ResourceContent> ReadResources(string folderPath) =>
        [.. Directory.EnumerateFiles(folderPath, "*.json", SearchOption.AllDirectories)
            .Select(path => new ResourceContent(
                Path.GetRelativePath(folderPath, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))];
}
