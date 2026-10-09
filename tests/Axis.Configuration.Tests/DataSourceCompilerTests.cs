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
            { "name": "customer", "type": "reference", "target": "Customer" }
          ] }
        """;

    private const string Customer = """
        { "id": "22222222-2222-4222-8222-222222222222", "kind": "entity", "name": "Customer", "formatVersion": 1,
          "displayField": "name", "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    private const string LineItem = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "entity", "name": "LineItem", "formatVersion": 1,
          "fields": [ { "name": "description", "type": "text" } ] }
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
    public void Path_that_is_not_a_field_of_the_entity_or_goes_through_a_reference_is_reported_at_that_path()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "total", "path": "total" }, { "name": "customerName", "path": "customer.name" }]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/orders.json", "/fields/1/path"),
                (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/orders.json", "/fields/2/path"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Contains("'total'", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("not supported yet", result.Diagnostics[1].Message, StringComparison.Ordinal);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(Guid.Parse(DataSourceId), diagnostic.ResourceId));
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
    public void Sort_that_names_no_projected_field_or_a_projected_reference_is_reported_at_the_sort(string sort)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource(
            "Order",
            """[{ "name": "number", "path": "number" }, { "name": "customer", "path": "customer" }]""",
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
    [InlineData("amount is null or date('2026-02-30') is null", ExpressionDiagnosticCodes.OutsideSqlSubset)]
    [InlineData("number ==", ExpressionDiagnosticCodes.SyntaxError)]
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

    [Fact]
    public void Valid_filter_is_in_the_model_checked_against_the_entity_fields()
    {
        // Field names in a filter match ignoring letter case.
        const string Filter = "Number != 'x' and amount is null or customer is not null";
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
    [InlineData(""", "parameters": [] """, "/parameters")]
    [InlineData(""", "aggregate": {} """, "/aggregate")]
    [InlineData(""", "pageSize": 101 """, "/pageSize")]
    [InlineData(""", "pageSize": 0 """, "/pageSize")]
    [InlineData(""", "sort": "--number" """, "/sort")]
    public void Property_not_built_yet_or_out_of_range_is_a_schema_violation(string extra, string path)
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource("Order", """[{ "name": "number", "path": "number" }]""", extra));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SchemaViolation, "data-sources/orders.json", path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Data_source_without_fields_is_a_schema_violation()
    {
        using var folder = Folder().With("data-sources/orders.json", DataSource("Order", "[]"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.SchemaViolation, "/fields"), (diagnostic.Code, diagnostic.Path));
    }

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/customer.json", Customer)
            .With("entities/order.json", Order);

    private static string DataSource(string entity, string fields, string extra = "") =>
        $$"""{ "id": "{{DataSourceId}}", "kind": "dataSource", "name": "Orders", "formatVersion": 1, "entity": "{{entity}}", "fields": {{fields}}{{extra}} }""";
}
