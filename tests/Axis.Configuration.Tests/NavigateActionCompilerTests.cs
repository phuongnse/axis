using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Expressions.Diagnostics;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class NavigateActionCompilerTests
{
    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "detail.title": "Detail", "order.open": "Open" } }
        """;

    private const string OrderParameter = """{ "name": "order", "type": "reference", "target": "Order", "required": true }""";

    private const string CodeParameter = """{ "name": "code", "type": "text", "required": true }""";

    private const string DetailId = "55555555-5555-4555-8555-555555555554";

    private const string OrdersFile = "pages/orders.json";

    [Fact]
    public void Row_action_passing_the_row_id_to_a_reference_parameter_compiles()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Empty(Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "Orders").Widgets).Actions);
    }

    [Fact]
    public void Form_action_passing_the_record_id_compiles_and_leaves_the_model_without_actions()
    {
        using var folder = Folder(
            """{ "type": "form", "entity": "Order", "actions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id" } } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Empty(Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "Orders").Widgets).Actions);
    }

    [Fact]
    public void Toolbar_action_to_a_page_without_parameters_compiles()
    {
        using var folder = Folder(
            """{ "type": "table", "entity": "Order", "toolbarActions": [ { "type": "navigate", "page": "CustomerForm", "label": { "textKey": "order.open" } } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Table_over_a_data_source_sees_its_projected_columns()
    {
        using var folder = Folder(
                """{ "type": "table", "dataSource": "OrderNumbers", "rowActions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "code": "label" } } ] }""",
                CodeParameter)
            .With("data-sources/order-numbers.json", OrderNumbers);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Table_over_a_data_source_does_not_see_a_root_field_it_does_not_project()
    {
        using var folder = Folder(
                """{ "type": "table", "dataSource": "OrderNumbers", "rowActions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "code": "quantity" } } ] }""",
                CodeParameter)
            .With("data-sources/order-numbers.json", OrderNumbers);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownName, OrdersFile, "/widgets/0/rowActions/0/parameters/code"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Action_naming_no_loaded_page_is_reported_at_its_page()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Missing", "label": { "textKey": "order.open" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigatePage, OrdersFile, "/widgets/0/rowActions/0/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'Missing'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Form_action_naming_no_loaded_page_is_reported_at_its_page()
    {
        using var folder = Folder(
            """{ "type": "form", "entity": "Order", "actions": [ { "type": "navigate", "page": "Missing", "label": { "textKey": "order.open" } } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigatePage, OrdersFile, "/widgets/0/actions/0/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Action_naming_a_page_that_failed_to_load_is_not_reported_again()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" } } ]"""))
            .With("pages/detail.json", $$"""{ "id": "{{DetailId}}", "kind": "page", "name": "Detail", "formatVersion": 1 }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("pages/detail.json", diagnostic.File));
    }

    [Fact]
    public void Parameter_the_target_page_does_not_declare_is_reported_at_its_key()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id", "extra": "id" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigateParameter, OrdersFile, "/widgets/0/rowActions/0/parameters/extra"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'extra'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameter_given_twice_ignoring_letter_case_is_reported_at_the_second_key()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id", "ORDER": "id" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigateParameter, OrdersFile, "/widgets/0/rowActions/0/parameters/ORDER"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Missing_required_parameter_is_reported_at_the_action()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingNavigateParameter, OrdersFile, "/widgets/0/rowActions/0"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'order'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_field_passed_to_a_reference_parameter_is_reported_at_the_parameter()
    {
        using var folder = Folder(
            """{ "type": "form", "entity": "Order", "actions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "number" } } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.ResultTypeMismatch, OrdersFile, "/widgets/0/actions/0/parameters/order"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Parameter_expression_that_does_not_parse_is_reported_at_the_parameter()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id +" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (OrdersFile, "/widgets/0/rowActions/0/parameters/order"),
            (diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Toolbar_expression_sees_no_names_not_even_the_id()
    {
        using var folder = Folder(
            """{ "type": "table", "entity": "Order", "toolbarActions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" }, "parameters": { "order": "id" } } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownName, OrdersFile, "/widgets/0/toolbarActions/0/parameters/order"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Navigate_label_with_a_text_key_no_locale_has_is_reported_at_its_label()
    {
        using var folder = Folder(Table("""[ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.missing" }, "parameters": { "order": "id" } } ]"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingTextKey, OrdersFile, "/widgets/0/rowActions/0/label/textKey"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Row_actions_on_a_table_over_a_grouped_data_source_are_reported_at_rowActions()
    {
        using var folder = Folder(
                """{ "type": "table", "dataSource": "OrderNumbers", "rowActions": [ { "type": "navigate", "page": "Detail", "label": { "textKey": "order.open" } } ] }""",
                CodeParameter)
            .With("data-sources/order-numbers.json", OrderNumbers.Replace(
                "\"fields\"",
                "\"aggregate\": { \"groupBy\": [\"label\"], \"measures\": [{ \"name\": \"orders\", \"function\": \"count\" }] }, \"fields\"",
                StringComparison.Ordinal));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/rowActions"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("group", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "type": "form", "entity": "Order", "rowActions": [ ACTION ] }""", "/widgets/0/rowActions")]
    [InlineData("""{ "type": "form", "entity": "Order", "toolbarActions": [ ACTION ] }""", "/widgets/0/toolbarActions")]
    [InlineData("""{ "type": "taskInbox", "rowActions": [ ACTION ] }""", "/widgets/0/rowActions")]
    [InlineData("""{ "type": "processInspector", "process": "OrderReview", "toolbarActions": [ ACTION ] }""", "/widgets/0/toolbarActions")]
    public void Row_and_toolbar_actions_on_a_widget_other_than_a_table_are_reported_at_their_property(string widget, string path)
    {
        const string Action = """{ "type": "navigate", "page": "CustomerForm", "label": { "textKey": "order.open" } }""";
        using var folder = Folder(widget.Replace("ACTION", Action, StringComparison.Ordinal))
            .With("processes/order-review.json", """
                { "id": "99999999-9999-4999-8999-999999999991", "kind": "process", "name": "OrderReview", "formatVersion": 1,
                  "entity": "Order", "start": "done", "steps": [ { "name": "done", "type": "end" } ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, path),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void Row_actions_on_a_container_widget_are_a_schema_error()
    {
        using var folder = Folder(
            """{ "type": "section", "title": { "textKey": "orders.title" }, "rowActions": [ { "type": "navigate", "page": "CustomerForm", "label": { "textKey": "order.open" } } ], "widgets": [ { "type": "table", "entity": "Order" } ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(OrdersFile, diagnostic.File));
    }

    [Theory]
    [InlineData("""{ "type": "navigate", "label": { "textKey": "order.open" } }""")]
    [InlineData("""{ "type": "navigate", "page": "Detail", "process": "OrderReview", "label": { "textKey": "order.open" } }""")]
    [InlineData("""{ "type": "startProcess", "process": "OrderReview", "page": "Detail", "label": { "textKey": "order.open" } }""")]
    [InlineData("""{ "type": "startProcess", "process": "OrderReview", "parameters": { "order": "id" }, "label": { "textKey": "order.open" } }""")]
    public void Action_with_a_property_of_the_other_kind_is_a_schema_error(string action)
    {
        using var folder = Folder($$"""{ "type": "form", "entity": "Order", "actions": [ {{action}} ] }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(OrdersFile, diagnostic.File));
    }

    // A reference parameter needs a target with a display field.
    private const string OrderEntity = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "displayField": "number",
          "fields": [ { "name": "number", "type": "text", "required": true }, { "name": "quantity", "type": "integer" } ] }
        """;

    private const string OrderNumbers = """
        { "id": "77777777-7777-4777-8777-777777777772", "kind": "dataSource", "name": "OrderNumbers", "formatVersion": 1, "entity": "Order",
          "fields": [ { "name": "label", "path": "number" } ] }
        """;

    private static string Table(string rowActions) => $$"""{ "type": "table", "entity": "Order", "rowActions": {{rowActions}} }""";

    /// <summary>
    /// The presentation folder with the page <c>Orders</c> holding <paramref name="widget"/>, and the
    /// page <c>Detail</c> with the given parameter, by default a required reference to <c>Order</c>.
    /// </summary>
    private static TemporaryFolder Folder(string widget, string parameter = OrderParameter) =>
        PresentationCompilerTests.Folder()
            .With("texts/en.json", TextsEn)
            .With("entities/order.json", OrderEntity)
            .With(OrdersFile, Page("Orders", widget))
            .With("pages/detail.json", Page("Detail", """{ "type": "table", "entity": "Order" }""", DetailId, "detail.title", parameter));
}
