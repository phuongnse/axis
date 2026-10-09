using System.Diagnostics;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class DataSourceCompilerTests
{
    private const string DataSourceId = "99999999-9999-4999-8999-999999999991";

    private const string Order = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1,
          "fields": [
            { "name": "number", "type": "text", "required": true },
            { "name": "amount", "type": "decimal" },
            { "name": "customer", "type": "reference", "target": "Customer" },
            { "name": "status", "type": "enum", "values": ["open", "closed"] },
            { "name": "paid", "type": "boolean" }
          ] }
        """;

    private const string Customer = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1,
          "displayField": "name", "fields": [
            { "name": "name", "type": "text", "required": true },
            { "name": "parent", "type": "reference", "target": "Customer" },
            { "name": "since", "type": "date" }
          ] }
        """;

    private const string LineItem = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "LineItem", "formatVersion": 1,
          "fields": [ { "name": "description", "type": "text" }, { "name": "amount", "type": "decimal" } ] }
        """;

    private const string Invoice = """
        { "id": "44444444-4444-4444-8444-444444444444", "kind": "entity", "name": "Invoice", "formatVersion": 1,
          "fields": [
            { "name": "number", "type": "text", "required": true },
            { "name": "lines", "type": "child-collection", "target": "LineItem" }
          ] }
        """;

    [Fact]
    public void Valid_data_source_is_in_the_model_with_its_entity_fields_sort_and_page_size_and_changes_the_content_hash()
    {
        using var withoutDataSource = Folder();
        // Entity and path resolve ignoring letter case, as entity references do.
        using var withDataSource = Folder().With("data-sources/orders.json", DataSource(
            "order",
            """[{ "name": "orderNumber", "path": "Number" }, { "name": "customer", "path": "customer" }]""",
            """, "sort": "-orderNumber", "pageSize": 50"""));

        var before = ApplicationCompiler.Compile(withoutDataSource.Path);
        var result = ApplicationCompiler.Compile(withDataSource.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Equal((Guid.Parse(DataSourceId), "Orders", "data-sources/orders.json"), (dataSource.Id, dataSource.Name, dataSource.File));
        Assert.Equal(new EntityReference(Guid.Parse("11111111-1111-4111-8111-111111111111"), "Order"), dataSource.Entity);
        Assert.Equal(
            [("orderNumber", "number", FieldType.Text), ("customer", "customer", FieldType.Reference)],
            dataSource.Fields.Select(field => (field.Name, field.Field.Name, field.Field.Type)));
        Assert.Equal("name", dataSource.Fields[1].Field.TargetDisplayField);
        Assert.Equal(new DataSourceSortModel("orderNumber", Descending: true), dataSource.Sort);
        Assert.Equal(50, dataSource.PageSize);
        Assert.True(result.Model.TryGetDataSource("ORDERS", out var found));
        Assert.Same(dataSource, found);
        Assert.NotNull(before.ContentHash);
        Assert.NotNull(result.ContentHash);
        Assert.NotEqual(before.ContentHash, result.ContentHash);
    }

    [Fact]
    public void Data_source_without_sort_or_page_size_has_no_sort_and_the_default_page_size()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource("Order", """[{ "name": "number", "path": "number" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Null(dataSource.Sort);
        Assert.Equal(20, dataSource.PageSize);
    }

    [Fact]
    public void Data_source_over_an_unknown_entity_is_reported_at_its_entity()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Invoice",
            """[{ "name": "number", "path": "missing" }]""",
            """, "sort": "missing" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        // The paths and the sort are not checked without the entity.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownDataSourceEntity, "data-sources/orders.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Invoice'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_over_an_entity_whose_file_was_not_loaded_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/invoice.json", """{ "id": "88888888-8888-4888-8888-888888888881", "kind": "entity", "name": "Invoice", "formatVersion": 1, "fields": "none" }""")
            .With("data-sources/orders.json", DataSource("Invoice", """[{ "name": "number", "path": "number" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "entities/invoice.json"), (diagnostic.Code, diagnostic.File));
    }

    [Fact]
    public void Path_through_references_is_in_the_model_with_one_field_per_hop()
    {
        // Each name of a path resolves ignoring letter case, and a path may take 3 hops.
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "customerName", "path": "Customer.name" }, { "name": "grandparent", "path": "customer.parent.PARENT" }, { "name": "far", "path": "customer.parent.parent.since" }]""",
            """, "sort": "-customerName" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Equal(["customer", "name"], dataSource.Fields[0].Path.Select(field => field.Name));
        Assert.Equal(("name", FieldType.Text), (dataSource.Fields[0].Field.Name, dataSource.Fields[0].Field.Type));
        Assert.Equal(["customer", "parent", "parent"], dataSource.Fields[1].Path.Select(field => field.Name));
        Assert.Equal("name", dataSource.Fields[1].Field.TargetDisplayField);
        Assert.Equal(["customer", "parent", "parent", "since"], dataSource.Fields[2].Path.Select(field => field.Name));
        Assert.Equal(new DataSourceSortModel("customerName", Descending: true), dataSource.Sort);
    }

    [Fact]
    public void Path_that_is_not_a_field_of_the_entity_is_reported_at_that_path()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "total", "path": "total" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/orders.json", "/fields/1/path"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'total'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("number.name", "'number', which is not a reference field of 'Order'")]
    [InlineData("customer.missing", "must name a field of the entity 'Customer'. 'missing' is not one")]
    [InlineData("customer.name.x", "'name', which is not a reference field of 'Customer'")]
    [InlineData("customer.parent.parent.parent.name", "takes 4 hops, at most 3 are allowed")]
    [InlineData("customer.parent.parent.parent.parent", "takes 4 hops, at most 3 are allowed")]
    public void Path_through_a_field_that_is_not_a_reference_to_an_unknown_field_or_with_too_many_hops_is_reported_at_that_path(
        string path, string messagePart)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            $$"""[{ "name": "number", "path": "number" }, { "name": "related", "path": "{{path}}" }]""",
            """, "sort": "related" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        // A sort naming the invalid path is not reported again.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/orders.json", "/fields/1/path"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{path}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Path_naming_a_child_collection_is_reported_at_that_path_and_its_sort_is_not_reported_again()
    {
        // A child collection has no column on the owner table, so it cannot be projected or sorted by.
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/line-item.json", LineItem)
            .With("entities/invoice.json", Invoice)
            .With("data-sources/invoices.json", DataSource(
                "Invoice",
                """[{ "name": "number", "path": "number" }, { "name": "lines", "path": "Lines" }]""",
                """, "sort": "lines" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/invoices.json", "/fields/1/path"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("child collection 'lines'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_over_a_child_entity_is_reported_at_its_entity_naming_the_owner()
    {
        // A child entity's rows are read only through its owner's child collection.
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/line-item.json", LineItem)
            .With("entities/invoice.json", Invoice)
            .With("data-sources/orders.json", DataSource("LineItem", """[{ "name": "description", "path": "description" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DataSourceOverChildEntity, "data-sources/orders.json", "/entity"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Contains("'LineItem'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'Invoice.lines'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_over_the_owner_of_a_child_entity_compiles()
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/line-item.json", LineItem)
            .With("entities/invoice.json", Invoice)
            .With("data-sources/orders.json", DataSource("Invoice", """[{ "name": "number", "path": "number" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Equal("Invoice", dataSource.Entity.Name);
    }

    [Fact]
    public void Repeated_projected_name_is_reported_at_the_later_name()
    {
        // Projected names compare exactly, so 'Number' is a name of its own.
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "Number", "path": "amount" }, { "name": "number", "path": "amount" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateDataSourceFieldName, "data-sources/orders.json", "/fields/2/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'number'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("-Number")]
    [InlineData("customer")]
    [InlineData("-customer")]
    [InlineData("parent")]
    public void Sort_that_names_no_projected_field_or_a_projected_reference_is_reported_at_the_sort(string sort)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "customer", "path": "customer" }, { "name": "parent", "path": "customer.parent" }]""",
            $$""", "sort": "{{sort}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceSort, "data-sources/orders.json", "/sort"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{sort}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Sort_naming_a_projected_field_with_an_invalid_path_is_not_reported_again()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "total", "path": "total" }]""",
            """, "sort": "total" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidDataSourceFieldPath, "/fields/0/path"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("amount", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("missing == 1", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("amount / 2 > 1", ExpressionDiagnosticCodes.OutsideSqlSubset)]
    [InlineData("lower(number) == 'a'", ExpressionDiagnosticCodes.OutsideSqlSubset)]
    [InlineData("amount > 0 and number == date('2026-02-30')", ExpressionDiagnosticCodes.TypeMismatch)]
    [InlineData("amount is null or date('2026-02-30') is null", ExpressionDiagnosticCodes.TypeMismatch)]
    [InlineData("amount is null or date('2026-13-45') is null", ExpressionDiagnosticCodes.TypeMismatch)]
    [InlineData("number ==", ExpressionDiagnosticCodes.SyntaxError)]
    [InlineData("customer.missing == 'a'", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("number.name == 'a'", ExpressionDiagnosticCodes.TypeMismatch)]
    [InlineData("customer.parent.parent.parent.name == 'a'", ExpressionDiagnosticCodes.TooManyHops)]
    public void Invalid_filter_is_reported_at_the_filter(string filter, string code)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "filter": "{{filter}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "data-sources/orders.json", "/filter"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    // A filter type-checks with the child collections, but no aggregate is in the SQL subset.
    [InlineData("sum(lines, amount) > 0", ExpressionDiagnosticCodes.OutsideSqlSubset)]
    [InlineData("number != 'x' and count(lines) > 0", ExpressionDiagnosticCodes.OutsideSqlSubset)]
    [InlineData("sum(lines, description) > 0", ExpressionDiagnosticCodes.TypeMismatch)]
    [InlineData("lines is null", ExpressionDiagnosticCodes.TypeMismatch)]
    // A path goes only through reference fields, so it cannot go through a child collection.
    [InlineData("lines.amount > 0", ExpressionDiagnosticCodes.TypeMismatch)]
    public void Filter_over_a_child_collection_is_reported_at_the_filter(string filter, string code)
    {
        using var folder = new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/line-item.json", LineItem)
            .With("entities/invoice.json", Invoice)
            .With("data-sources/invoices.json", DataSource(
                "Invoice",
                """[{ "name": "number", "path": "number" }]""",
                $$""", "filter": "{{filter}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "data-sources/invoices.json", "/filter"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Valid_filter_is_in_the_model_checked_against_the_entity_fields()
    {
        // Field names in a filter match ignoring letter case, also along a path through references.
        const string Filter = "Number != 'x' and amount is null or customer is not null and Customer.parent.NAME == 'a'";
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "filter": "{{Filter}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var filter = Assert.Single(result.Model.DataSources).Filter;
        Assert.NotNull(filter);
        Assert.Equal(Filter, filter.Expression);
        Assert.True(filter.Check.Succeeded);
        Assert.Equal("boolean", filter.Check.Type.ToString());
    }

    [Fact]
    public void Filter_over_an_unknown_entity_is_not_checked()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Invoice",
            """[{ "name": "number", "path": "number" }]""",
            """, "filter": "missing" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownDataSourceEntity, "/entity"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData(""", "parameters": [{ "name": "p", "type": "child-collection" }] """, "/parameters/0/type")]
    [InlineData(""", "aggregate": { "groupBy": ["number"] } """, "/aggregate")]
    [InlineData(""", "aggregate": { "groupBy": ["number"], "measures": [] } """, "/aggregate/measures")]
    [InlineData(""", "aggregate": { "groupBy": ["number"], "measures": [{ "name": "a", "function": "avg", "field": "number" }] } """, "/aggregate/measures/0/function")]
    [InlineData(""", "aggregate": { "groupBy": ["number", "number"], "measures": [{ "name": "n", "function": "count" }] } """, "/aggregate/groupBy")]
    [InlineData(""", "pageSize": 101 """, "/pageSize")]
    [InlineData(""", "pageSize": 0 """, "/pageSize")]
    [InlineData(""", "sort": "--number" """, "/sort")]
    public void Malformed_or_out_of_range_property_is_a_schema_violation(string extra, string path)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource("Order", """[{ "name": "number", "path": "number" }]""", extra));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "data-sources/orders.json", path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""[{ "name": "a", "type": "text" }, { "name": "A", "type": "integer" }]""", 1, "'A'")]
    [InlineData("""[{ "name": "Page", "type": "integer" }]""", 0, "'Page'")]
    [InlineData("""[{ "name": "SORT", "type": "text" }]""", 0, "'SORT'")]
    [InlineData("""[{ "name": "pagesize", "type": "integer" }]""", 0, "'pagesize'")]
    [InlineData("""[{ "name": "Amount", "type": "decimal" }]""", 0, "field of the entity 'Order'")]
    public void Parameter_name_that_repeats_another_is_reserved_or_is_a_field_is_reported_at_that_name(
        string parameters, int index, string messagePart)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "parameters": {{parameters}} """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceParameterName, "data-sources/orders.json", $"/parameters/{index}/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "p", "type": "text", "values": ["x"] }""", DiagnosticCodes.InvalidConstraint, "/parameters/0/values")]
    [InlineData("""{ "name": "p", "type": "enum" }""", DiagnosticCodes.MissingTypeProperty, "/parameters/0")]
    [InlineData("""{ "name": "p", "type": "reference" }""", DiagnosticCodes.MissingTypeProperty, "/parameters/0")]
    [InlineData("""{ "name": "p", "type": "reference", "target": "Missing" }""", DiagnosticCodes.UnknownReferenceTarget, "/parameters/0/target")]
    [InlineData("""{ "name": "p", "type": "reference", "target": "Order" }""", DiagnosticCodes.ReferenceTargetWithoutDisplayField, "/parameters/0/target")]
    [InlineData("""{ "name": "p", "type": "text", "label": { "textKey": "orders.missing" } }""", DiagnosticCodes.MissingTextKey, "/parameters/0/label/textKey")]
    public void Parameter_type_properties_and_label_are_checked_as_for_entity_fields(string parameter, string code, string path)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "parameters": [{{parameter}}] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "data-sources/orders.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Parameters_are_in_the_model_and_the_filter_sees_them_as_plain_names()
    {
        // The enum parameter's values are a subset of the field's, and the reference target resolves ignoring letter case.
        const string StatusFilter = """{ "name": "statusFilter", "type": "enum", "values": ["open"] }""";
        const string CustomerFilter = """{ "name": "customerFilter", "type": "reference", "target": "customer", "required": true }""";
        const string Filter = "(StatusFilter is null or status == statusFilter) and (customerFilter is null or customerFilter == customer)";
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "parameters": [{{StatusFilter}}, {{CustomerFilter}}], "filter": "{{Filter}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.Equal(2, dataSource.Parameters.Count);
        var status = dataSource.Parameters[0];
        Assert.Equal(("statusFilter", FieldType.Enum, false, null), (status.Name, status.Type, status.Required, status.Target));
        Assert.Equal(["open"], status.Values);
        var customer = dataSource.Parameters[1];
        Assert.Equal(
            ("customerFilter", FieldType.Reference, true, new EntityReference(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Customer")),
            (customer.Name, customer.Type, customer.Required, customer.Target));
        Assert.NotNull(dataSource.Filter);
        Assert.True(dataSource.Filter.Check.Succeeded);
    }

    [Fact]
    public void Path_that_starts_at_a_reference_parameter_is_a_type_mismatch_at_the_filter()
    {
        // A reference parameter is an id, not a row the query can join.
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            """, "parameters": [{ "name": "customerFilter", "type": "reference", "target": "Customer" }], "filter": "customerFilter.name == 'a'" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("cannot follow the parameter 'customerFilter'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("status == statusFilter")]
    [InlineData("statusFilter != status")]
    public void Enum_parameter_with_a_value_the_field_lacks_is_a_type_mismatch_at_the_filter(string filter)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "parameters": [{ "name": "statusFilter", "type": "enum", "values": ["open", "archived"] }], "filter": "{{filter}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Filter_is_not_checked_while_a_parameter_has_a_diagnostic()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            """, "parameters": [{ "name": "statusFilter", "type": "enum" }], "filter": "missing == statusFilter" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTypeProperty, "/parameters/0"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Filter_that_calls_a_rule_in_the_sql_subset_compiles()
    {
        using var folder = Folder()
            .With("rules/is-large.json", IsLarge)
            .With("data-sources/orders.json", DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """, "filter": "isLarge(amount)" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var filter = Assert.Single(result.Model.DataSources).Filter;
        Assert.NotNull(filter);
        Assert.Equal("IsLarge", Assert.Single(filter.Check.RuleCalls).Value.Name);
    }

    [Fact]
    public void Filter_that_calls_a_rule_outside_the_sql_subset_names_the_rule()
    {
        using var folder = Folder()
            .With("rules/is-x.json", """
                { "id": "66666666-6666-4666-8666-666666666602", "kind": "rule", "name": "IsX", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "text" }], "resultType": "boolean", "expression": "lower(value) == 'x'" }
                """)
            .With("data-sources/orders.json", DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """, "filter": "IsX(number)" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.OutsideSqlSubset, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'IsX'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'lower'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_that_calls_a_rule_with_a_wrong_argument_is_a_type_mismatch()
    {
        using var folder = Folder()
            .With("rules/is-large.json", IsLarge)
            .With("data-sources/orders.json", DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """, "filter": "IsLarge('a')" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.TypeMismatch, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Filter_that_calls_a_rule_with_its_own_error_adds_no_diagnostic()
    {
        using var folder = Folder()
            .With("rules/broken.json", """
                { "id": "66666666-6666-4666-8666-666666666603", "kind": "rule", "name": "Broken", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean", "expression": "value >" }
                """)
            .With("data-sources/orders.json", DataSource(
                "Order",
                """[{ "name": "number", "path": "number" }]""",
                """, "filter": "Broken(amount)" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.SyntaxError, "rules/broken.json", "/expression"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Filter_whose_rule_calls_inline_past_the_cap_is_outside_the_subset()
    {
        // L8 calls L7 four times, and so on down to L0, so inlined it is far past 2,000 nodes.
        using var folder = Folder().With("rules/l0.json", """
            { "id": "77777777-7777-4777-8777-777777777700", "kind": "rule", "name": "L0", "formatVersion": 1,
              "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean", "expression": "value > 0" }
            """);
        for (var k = 1; k <= 8; k++)
        {
            var call = $"L{k - 1}(value)";
            folder.With($"rules/l{k}.json", $$"""
                { "id": "77777777-7777-4777-8777-7777777777{{k:00}}", "kind": "rule", "name": "L{{k}}", "formatVersion": 1,
                  "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean",
                  "expression": "{{call}} and {{call}} and {{call}} and {{call}}" }
                """);
        }

        folder.With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            """, "filter": "L8(amount)" """));
        var stopwatch = Stopwatch.StartNew();

        var result = ApplicationCompiler.Compile(folder.Path);

        stopwatch.Stop();
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.OutsideSqlSubset, "data-sources/orders.json", "/filter"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'L8'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public void Data_source_without_fields_is_a_schema_violation()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource("Order", "[]"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "/fields"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Valid_aggregate_is_in_the_model_with_its_group_fields_measures_and_sort()
    {
        // A group field may be a reference, and a measure may read a field through a reference.
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "customer", "path": "customer" }, { "name": "status", "path": "status" }, { "name": "amount", "path": "amount" }, { "name": "since", "path": "customer.since" }]""",
            """
            , "aggregate": { "groupBy": ["status", "customer"], "measures": [
                { "name": "orders", "function": "count" },
                { "name": "amountSum", "function": "sum", "field": "amount" },
                { "name": "firstSince", "function": "min", "field": "since" },
                { "name": "amountMax", "function": "max", "field": "amount" }
              ] }, "sort": "-amountSum"
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var dataSource = Assert.Single(result.Model.DataSources);
        Assert.NotNull(dataSource.Aggregate);
        Assert.Equal([dataSource.Fields[1], dataSource.Fields[0]], dataSource.Aggregate.GroupBy);
        Assert.Equal(
            [
                ("orders", AggregateFunction.Count, null),
                ("amountSum", AggregateFunction.Sum, dataSource.Fields[2]),
                ("firstSince", AggregateFunction.Min, dataSource.Fields[3]),
                ("amountMax", AggregateFunction.Max, dataSource.Fields[2]),
            ],
            dataSource.Aggregate.Measures.Select(measure => (measure.Name, measure.Function, measure.Field)));
        Assert.Equal(new DataSourceSortModel("amountSum", Descending: true), dataSource.Sort);
        Assert.True(dataSource.TryGetMeasure("amountSum", out var measure));
        Assert.Same(dataSource.Aggregate.Measures[1], measure);
        Assert.False(dataSource.TryGetMeasure("AmountSum", out _));
    }

    [Fact]
    public void Aggregate_with_no_group_field_and_a_sort_by_a_group_field_compiles()
    {
        using var folder = Folder()
            .With("data-sources/orders.json", DataSource(
                "Order",
                """[{ "name": "amount", "path": "amount" }]""",
                """, "aggregate": { "groupBy": [], "measures": [{ "name": "total", "function": "sum", "field": "amount" }] } """))
            .With("data-sources/by-status.json", DataSource(
                "Order",
                """[{ "name": "status", "path": "status" }]""",
                """, "aggregate": { "groupBy": ["status"], "measures": [{ "name": "orders", "function": "count" }] }, "sort": "-status" """)
                .Replace(DataSourceId, "99999999-9999-4999-8999-999999999992", StringComparison.Ordinal)
                .Replace("\"Orders\"", "\"OrdersByStatus\"", StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Empty(Assert.Single(result.Model.DataSources, dataSource => dataSource.Name == "Orders").Aggregate!.GroupBy);
        Assert.Equal(
            new DataSourceSortModel("status", Descending: true),
            Assert.Single(result.Model.DataSources, dataSource => dataSource.Name == "OrdersByStatus").Sort);
    }

    [Theory]
    [InlineData("""{ "name": "m", "function": "sum", "field": "number" }""", "'sum' cannot take the text field 'number'")]
    [InlineData("""{ "name": "m", "function": "sum", "field": "since" }""", "'sum' cannot take the date field 'since'")]
    [InlineData("""{ "name": "m", "function": "min", "field": "paid" }""", "'min' cannot take the boolean field 'paid'")]
    [InlineData("""{ "name": "m", "function": "max", "field": "status" }""", "'max' cannot take the enum field 'status'")]
    [InlineData("""{ "name": "m", "function": "max", "field": "customer" }""", "'max' cannot take the reference field 'customer'")]
    [InlineData("""{ "name": "m", "function": "sum", "field": "Amount" }""", "'Amount' must name a projected field")]
    [InlineData("""{ "name": "m", "function": "min", "field": "missing" }""", "'missing' must name a projected field")]
    public void Measure_field_of_the_wrong_type_or_not_projected_is_reported_at_its_field(string measure, string message)
    {
        using var folder = Folder().With("data-sources/orders.json", Grouped(measure));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceMeasure, "data-sources/orders.json", "/aggregate/measures/1/field"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "m", "function": "count", "field": "amount" }""", "'count' counts rows and takes no 'field'")]
    [InlineData("""{ "name": "m", "function": "sum" }""", "'sum' needs a 'field'")]
    [InlineData("""{ "name": "m", "function": "max" }""", "'max' needs a 'field'")]
    public void Count_with_a_field_or_another_function_without_one_is_reported_at_the_measure(string measure, string message)
    {
        using var folder = Folder().With("data-sources/orders.json", Grouped(measure));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceMeasure, "/aggregate/measures/1"),
            (diagnostic.Code, diagnostic.Path));
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("Number")]
    public void Group_field_that_names_no_projected_field_is_reported_at_that_group_field(string groupField)
    {
        // Group fields match projected names exactly. A sort that names the unknown group field is
        // not reported again.
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }]""",
            $$""", "aggregate": { "groupBy": ["number", "{{groupField}}"], "measures": [{ "name": "orders", "function": "count" }] }, "sort": "{{groupField}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownDataSourceGroupField, "data-sources/orders.json", "/aggregate/groupBy/1"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{groupField}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""[{ "name": "status", "function": "count" }]""", 0)]
    [InlineData("""[{ "name": "orders", "function": "count" }, { "name": "orders", "function": "sum", "field": "amount" }]""", 1)]
    public void Measure_name_that_is_a_group_field_or_repeats_another_measure_is_reported_at_that_name(string measures, int index)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "status", "path": "status" }, { "name": "amount", "path": "amount" }]""",
            $$""", "aggregate": { "groupBy": ["status"], "measures": {{measures}} } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.DuplicateDataSourceMeasureName, "data-sources/orders.json", $"/aggregate/measures/{index}/name"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("amount", "must name a group field or a measure")]
    [InlineData("-amount", "must name a group field or a measure")]
    [InlineData("Orders", "must name a group field or a measure")]
    [InlineData("customer", "names the reference field 'customer'")]
    public void Grouped_sort_that_names_no_group_field_or_measure_or_a_group_reference_is_reported_at_the_sort(string sort, string message)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "customer", "path": "customer" }, { "name": "amount", "path": "amount" }]""",
            $$""", "aggregate": { "groupBy": ["customer"], "measures": [{ "name": "orders", "function": "count" }] }, "sort": "{{sort}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceSort, "data-sources/orders.json", "/sort"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Measure_over_a_projected_field_with_an_invalid_path_is_not_reported_again()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "status", "path": "status" }, { "name": "total", "path": "total" }]""",
            """, "aggregate": { "groupBy": ["status"], "measures": [{ "name": "totalSum", "function": "sum", "field": "total" }] } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidDataSourceFieldPath, "/fields/1/path"), (diagnostic.Code, diagnostic.Path));
    }

    /// <summary>
    /// A data source grouped by <c>status</c> whose measures are a count, then <paramref name="measure"/>,
    /// over every scalar kind: text, decimal, enum, reference, boolean and date.
    /// </summary>
    private static string Grouped(string measure) =>
        DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "amount", "path": "amount" }, { "name": "status", "path": "status" }, { "name": "customer", "path": "customer" }, { "name": "paid", "path": "paid" }, { "name": "since", "path": "customer.since" }]""",
            $$""", "aggregate": { "groupBy": ["status"], "measures": [{ "name": "orders", "function": "count" }, {{measure}}] } """);

    private const string IsLarge = """
        { "id": "66666666-6666-4666-8666-666666666601", "kind": "rule", "name": "IsLarge", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "decimal" }], "resultType": "boolean", "expression": "value > 1000" }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/customer.json", Customer)
            .With("entities/order.json", Order);

    private static string DataSource(string entity, string fields, string extra = "") =>
        $$"""{ "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Orders", "formatVersion": 1, "entity": "{{entity}}", "fields": {{fields}}{{extra}} }""";
}
