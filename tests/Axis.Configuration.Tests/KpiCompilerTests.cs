using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Diagnostics;
using static Axis.Configuration.Tests.PresentationCompilerTests;

namespace Axis.Configuration.Tests;

public sealed class KpiCompilerTests
{
    private const string TextsEn = """
        { "id": "33333333-3333-4333-8333-333333333333", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders", "orderForm.title": "Order",
                     "customerForm.title": "Customer", "detail.title": "Detail", "kpi.orders": "Orders", "kpi.quantity": "Quantity",
                     "kpi.show": "Show" } }
        """;

    private const string OrderEntity = """
        { "id": "11111111-1111-4111-8111-111111111111", "kind": "entity", "name": "Order", "formatVersion": 1, "displayField": "number",
          "fields": [ { "name": "number", "type": "text", "required": true }, { "name": "quantity", "type": "integer" } ] }
        """;

    private const string DetailId = "55555555-5555-4555-8555-555555555554";

    private const string OrdersFile = "pages/orders.json";

    private const string Valid = """
        { "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" },
          "comparison": { "measure": "quantitysum", "label": { "textKey": "kpi.quantity" } },
          "navigate": { "type": "navigate", "page": "CustomerForm", "label": { "textKey": "kpi.show" } } }
        """;

    [Fact]
    public void Kpi_compiles_to_its_data_source_and_the_measures_as_the_data_source_declares_them()
    {
        using var folder = Folder(Valid);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var widget = Assert.Single(Assert.Single(result.Model.Pages, page => page.Name == "Orders").Widgets);
        var customerForm = Assert.Single(result.Model.Pages, page => page.Name == "CustomerForm");
        Assert.Equal(WidgetType.Kpi, widget.Type);
        Assert.Equal(new DataSourceReference(Guid.Parse("77777777-7777-4777-8777-777777777773"), "OrderTotals"), widget.DataSource);
        Assert.Null(widget.Entity);
        Assert.Null(widget.Form);
        Assert.Empty(widget.Actions);
        Assert.Equal(
            new KpiModel(
                "orders",
                new TextReference("kpi.orders"),
                new KpiComparisonModel("quantitySum", new TextReference("kpi.quantity")),
                new NavigateActionModel(new PageReference(customerForm.Id, "CustomerForm"), new TextReference("kpi.show"))),
            widget.Kpi);
    }

    [Fact]
    public void Kpi_without_a_comparison_or_navigate_compiles_to_a_model_with_neither()
    {
        using var folder = Folder("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "ORDERS", "label": { "textKey": "kpi.orders" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new KpiModel("orders", new TextReference("kpi.orders"), null, null), widget.Kpi);
    }

    [Fact]
    public void Other_widgets_have_no_kpi()
    {
        using var folder = Folder(Valid);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.All(
            result.Model!.Pages.Where(page => page.Name != "Orders").SelectMany(page => page.Widgets),
            widget => Assert.Null(widget.Kpi));
    }

    [Fact]
    public void Kpi_does_not_count_toward_the_one_table_and_the_one_record_widget_of_a_page()
    {
        using var folder = Folder(
            """
            { "type": "table", "entity": "Order" },
            { "type": "form", "entity": "Order" },
            { "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" } },
            { "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" } }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Theory]
    [InlineData("OrderList")]
    [InlineData("OrderByNumber")]
    public void Kpi_over_a_data_source_that_is_not_one_total_row_is_reported_at_its_data_source(string dataSource)
    {
        using var folder = Folder($$"""{ "type": "kpi", "dataSource": "{{dataSource}}", "measure": "orders", "label": { "textKey": "kpi.orders" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.KpiDataSourceNotTotal, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{dataSource}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "missing", "label": { "textKey": "kpi.orders" } }""", "/widgets/0/measure", "missing")]
    [InlineData(
        """{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "comparison": { "measure": "other", "label": { "textKey": "kpi.quantity" } } }""",
        "/widgets/0/comparison/measure",
        "other")]
    public void Kpi_naming_a_measure_its_data_source_lacks_is_reported_at_that_property(string widget, string path, string measure)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownKpiMeasure, OrdersFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{measure}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("entity", "\"Order\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("form", "\"OrderEdit\"", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("actions", "[]", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("rowActions", "[]", DiagnosticCodes.InvalidWidgetBinding)]
    [InlineData("toolbarActions", "[]", DiagnosticCodes.InvalidWidgetBinding)]
    public void Kpi_naming_an_entity_a_form_or_actions_is_reported_at_that_property(string property, string value, string code)
    {
        using var folder = Folder($$"""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "{{property}}": {{value}} }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, OrdersFile, $"/widgets/0/{property}"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains($"'{property}'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Kpi_over_a_data_source_with_a_required_parameter_is_reported_naming_the_parameter()
    {
        using var folder = Folder(
            """{ "type": "kpi", "dataSource": "FilteredTotals", "measure": "orders", "label": { "textKey": "kpi.orders" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidWidgetBinding, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("'numberFilter'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Kpi_naming_no_loaded_data_source_is_reported_at_its_data_source()
    {
        using var folder = Folder("""{ "type": "kpi", "dataSource": "Missing", "measure": "orders", "label": { "textKey": "kpi.orders" } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownWidgetDataSource, OrdersFile, "/widgets/0/dataSource"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.missing" } }""", "/widgets/0/label/textKey")]
    [InlineData(
        """{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "comparison": { "measure": "orders", "label": { "textKey": "kpi.missing" } } }""",
        "/widgets/0/comparison/label/textKey")]
    public void Kpi_label_without_a_text_is_reported_at_its_text_key(string widget, string path)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.MissingTextKey, OrdersFile, path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Navigate_to_an_unknown_page_is_reported_at_its_page()
    {
        using var folder = Folder(
            """{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "navigate": { "type": "navigate", "page": "Missing", "label": { "textKey": "kpi.show" } } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.UnknownNavigatePage, OrdersFile, "/widgets/0/navigate/page"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void Navigate_of_a_kpi_is_checked_even_when_its_data_source_is_unknown()
    {
        using var folder = Folder(
            """{ "type": "kpi", "dataSource": "Missing", "measure": "orders", "label": { "textKey": "kpi.orders" }, "navigate": { "type": "navigate", "page": "Gone", "label": { "textKey": "kpi.show" } } }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownWidgetDataSource, "/widgets/0/dataSource"),
                (DiagnosticCodes.UnknownNavigatePage, "/widgets/0/navigate/page"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
    }

    [Fact]
    public void Navigate_parameter_naming_a_page_parameter_compiles_with_the_declared_names()
    {
        using var folder = Folder(
            """{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "navigate": { "type": "navigate", "page": "Detail", "label": { "textKey": "kpi.show" }, "parameters": { "ORDER": "order" } } }""",
            pageParameters: """{ "name": "order", "type": "reference", "target": "Order", "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var widget = Assert.Single(Assert.Single(result.Model!.Pages, page => page.Name == "Orders").Widgets);
        Assert.Equal(new Dictionary<string, string> { ["order"] = "order" }, widget.Kpi!.Navigate!.Parameters);
    }

    [Fact]
    public void Navigate_parameter_naming_id_is_reported_because_a_kpi_has_no_record()
    {
        using var folder = Folder(
            """{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "navigate": { "type": "navigate", "page": "Detail", "label": { "textKey": "kpi.show" }, "parameters": { "order": "id" } } }""",
            pageParameters: """{ "name": "order", "type": "reference", "target": "Order", "required": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (ExpressionDiagnosticCodes.UnknownName, OrdersFile, "/widgets/0/navigate/parameters/order"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "label": { "textKey": "kpi.orders" } }""")]
    [InlineData("""{ "type": "kpi", "measure": "orders", "label": { "textKey": "kpi.orders" } }""")]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders" }""")]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "comparison": { "measure": "orders" } }""")]
    [InlineData("""{ "type": "kpi", "dataSource": "OrderTotals", "measure": "orders", "label": { "textKey": "kpi.orders" }, "parameters": {} }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "measure": "orders" }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "label": { "textKey": "kpi.orders" } }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "comparison": { "measure": "orders", "label": { "textKey": "kpi.orders" } } }""")]
    [InlineData("""{ "type": "table", "entity": "Order", "navigate": { "type": "navigate", "page": "Detail", "label": { "textKey": "kpi.show" } } }""")]
    public void Kpi_with_a_missing_property_or_another_widget_with_a_kpi_property_violates_the_page_schema(string widget)
    {
        using var folder = Folder(widget);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, OrdersFile), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    /// <summary>
    /// The presentation folder with data sources over orders and the page <c>Orders</c> holding
    /// <paramref name="widgets"/>, and the page <c>Detail</c> with the given parameters.
    /// </summary>
    private static TemporaryFolder Folder(string widgets, string? pageParameters = null) =>
        PresentationCompilerTests.Folder()
            .With("texts/en.json", TextsEn)
            .With("entities/order.json", OrderEntity)
            .With("data-sources/order-list.json", """
                { "id": "77777777-7777-4777-8777-777777777771", "kind": "dataSource", "name": "OrderList", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "number", "path": "number" } ] }
                """)
            .With("data-sources/order-by-number.json", """
                { "id": "77777777-7777-4777-8777-777777777772", "kind": "dataSource", "name": "OrderByNumber", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "number", "path": "number" } ],
                  "aggregate": { "groupBy": ["number"], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With("data-sources/order-totals.json", """
                { "id": "77777777-7777-4777-8777-777777777773", "kind": "dataSource", "name": "OrderTotals", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "quantity", "path": "quantity" } ],
                  "aggregate": { "groupBy": [], "measures": [ { "name": "orders", "function": "count" }, { "name": "quantitySum", "function": "sum", "field": "quantity" } ] } }
                """)
            .With("data-sources/filtered-totals.json", """
                { "id": "77777777-7777-4777-8777-777777777774", "kind": "dataSource", "name": "FilteredTotals", "formatVersion": 1, "entity": "Order",
                  "fields": [ { "name": "quantity", "path": "quantity" } ],
                  "parameters": [ { "name": "numberFilter", "type": "text", "required": true } ],
                  "aggregate": { "groupBy": [], "measures": [ { "name": "orders", "function": "count" } ] } }
                """)
            .With(OrdersFile, Page("Orders", widgets, parameters: pageParameters))
            .With("pages/detail.json", Page("Detail", """{ "type": "table", "entity": "Order" }""", DetailId, "detail.title", pageParameters));
}
